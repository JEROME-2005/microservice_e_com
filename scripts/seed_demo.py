#!/usr/bin/env python3
"""LankaMart demo data (build guide §1.17.2).

Creates: 1 approved seller (+ 25 published products with stock), 1 verified customer with an address.  Safe to re-run.

    pip install requests
    python scripts/seed_demo.py            # needs the backend running in Development (Aspire AppHost or compose + dotnet run)

Talks ONLY to the gateway (http://localhost:5100) — exactly like a real client would, so it doubles as a smoke test.
Differences from the guide's version: retries when the gateway's auth rate limit (10/min) answers 429, derives SKUs from
the item's fixed position (a half-finished run can be re-run without SKU clashes) and waits for the SellerApproved event
to reach Catalog instead of retrying only once.
"""
import sys
import time
import urllib.parse

import requests

BASE = "http://localhost:5100/api/v1"
PASSWORD = "Sup3rSecret!!"
S = requests.Session()


def call(method, path, token=None, **kw):
    headers = kw.pop("headers", {})
    if token:
        headers["Authorization"] = f"Bearer {token}"
    for attempt in range(8):
        r = S.request(method, BASE + path, headers=headers, timeout=20, **kw)
        if r.status_code != 429 or "ACCOUNT_LOCKED" in r.text:
            return r
        wait = 10 if attempt < 7 else 0
        print(f"  (rate limited by the gateway on {path}; waiting {wait}s)")
        time.sleep(wait)
    return r


def dev_otp(destination):
    for _ in range(10):
        r = call("GET", "/dev/otp/" + urllib.parse.quote(destination, safe=""))
        if r.status_code == 200:
            return r.json()["code"]
        time.sleep(1)
    sys.exit(f"no dev OTP for {destination} - is Identity running in Development?")


def login(email, password):
    r = call("POST", "/auth/login", json=dict(email=email, password=password))
    r.raise_for_status()
    return r.json()["accessToken"]


def ensure_user(email, phone, name, role):
    r = call("POST", "/auth/register", json=dict(email=email, phone=phone, password=PASSWORD, fullName=name, role=role))
    if r.status_code == 202:
        e164 = "+94" + phone[1:]
        for channel, dest in (("email", email), ("phone", e164)):
            code = dev_otp(dest)
            call("POST", "/auth/verify-otp", json=dict(email=email, channel=channel, code=code)).raise_for_status()
        print(f"  registered + verified {email}")
    elif r.status_code != 409:
        sys.exit(f"register failed: {r.status_code} {r.text}")
    return login(email, PASSWORD)


def wait_for(path, token, seconds=30):
    for _ in range(seconds):
        if call("GET", path, token).status_code == 200:
            return
        time.sleep(1)
    sys.exit(f"{path} never became available - is the consumer running?")


CATALOGUE = {   # category slug → [(name, brand, price in LKR)]
    "electronics": [("Galaxy A15 Smartphone", "Samsung", 62999), ("Wireless Earbuds Pro", "SoundCore", 8999), ("20000mAh Power Bank", "Anker", 6499)],
    "fashion": [("Running Shoes Air Zoom", "Nike", 18500), ("Classic Cotton T-Shirt", "Odel", 1850), ("Slim Fit Jeans", "Cotton Collection", 5400),
                ("DEMO Pending-Payment Test Item (total ends .99)", "Demo", 1149.99)],   # 114,999 + 35,000 delivery = 149,999 → ends .99 → pending webhook
    "home-living": [("Stainless Steel Water Bottle 1L", "Hydro", 2400), ("Rice Cooker 1.8L", "Singer", 11900), ("Cotton Bed Sheet Set", "Linea", 7800)],
    "beauty-health": [("Herbal Face Wash", "Ayur", 950), ("Vitamin C Serum", "Glow", 3200), ("Electric Toothbrush", "Oral-B", 9800)],
    "groceries": [("Ceylon Black Tea 400g", "Mlesna", 1250), ("Basmati Rice 5kg", "Araliya", 2650), ("Coconut Oil 1L", "Marina", 1450)],
    "sports-outdoors": [("Yoga Mat 6mm", "Decathlon", 3900), ("Badminton Racket Set", "Yonex", 7500), ("Cricket Bat English Willow", "Gunn", 23500)],
    "books-stationery": [("A5 Hardcover Notebook", "Atlas", 650), ("Gel Pen Pack of 10", "Pilot", 890), ("Scientific Calculator", "Casio", 4300)],
    "toys-baby": [("Wooden Building Blocks 100pc", "Playtime", 3600), ("Baby Stroller Lite", "Chicco", 28900), ("Remote Control Car", "Hot Wheels", 4700)],
}


def main():
    print("Seller ...")
    seller = ensure_user("seller1@lankamart.local", "0771000001", "Kamal Fernando", "seller")
    me = call("GET", "/auth/me", seller).json()
    wait_for("/sellers/me", seller)                      # profile is created asynchronously by the UserRegistered event
    st = call("GET", "/sellers/me", seller).json()["status"]
    if st in ("Registered", "Rejected"):
        kyc = dict(businessName="Fernando Traders", businessType="registered", brNumber="PV 12345", goodsCategory="General",
                   nicNumber="901234567V", addressLine="45 Main Street", district="Colombo",
                   bankName="Commercial Bank", bankBranch="Colombo 03", bankAccountName="Fernando Traders", bankAccountNumber="8001234567")
        call("POST", "/sellers/kyc", seller, json=kyc).raise_for_status()
        print("  KYC submitted")
        st = "KycSubmitted"
    if st in ("KycSubmitted", "UnderReview"):
        admin = login("sellermgr@lankamart.local", "Manager#12345678")
        call("POST", f"/admin/sellers/{me['userId']}/approve", admin).raise_for_status()
        print("  seller approved by the Seller Manager")
    elif st != "Active":
        sys.exit(f"seller is {st}; reinstate it as the Seller Manager first")

    wait_for("/seller/products", seller)                 # 403 SELLER_NOT_APPROVED until SellerApproved reaches Catalog
    cats = {c["slug"]: c["id"] for c in call("GET", "/categories").json()}
    existing = {p["name"] for p in call("GET", "/seller/products", seller).json()}

    n = idx = 0
    for slug, items in CATALOGUE.items():
        for name, brand, price in items:
            idx += 1                                     # fixed position → stable SKUs across re-runs
            if name in existing:
                continue
            cents = round(price * 100)
            variants = [dict(sku=f"{slug[:3].upper()}-{idx:03d}-A", attributes={"edition": "Standard"}, priceCents=cents)]
            if slug == "fashion" and "DEMO" not in name:
                variants = [dict(sku=f"FSH-{idx:03d}-{s}", attributes={"size": s}, priceCents=cents) for s in ("S", "M", "L")]
            if slug == "electronics":
                variants.append(dict(sku=f"ELE-{idx:03d}-B", attributes={"edition": "Plus"}, priceCents=round(cents * 1.15)))
            body = dict(categoryId=cats[slug], brand=brand,
                        translations=[dict(lang="en", name=name, description=f"{name} by {brand}. Genuine product with warranty.")],
                        variants=variants, images=[dict(url=f"https://picsum.photos/seed/{idx}/600/600", alt=name)])
            r = call("POST", "/seller/products", seller, json=body)
            r.raise_for_status()
            call("POST", f"/seller/products/{r.json()['id']}/publish", seller).raise_for_status()
            n += 1
    print(f"  published {n} new products (each variant starts with demo stock in Development)")

    print("Customer ...")
    cust = ensure_user("customer1@lankamart.local", "0771000002", "Nimal Perera", "customer")
    wait_for("/customers/me", cust)
    if not call("GET", "/customers/me/addresses", cust).json():
        call("POST", "/customers/me/addresses", cust, json=dict(label="Home", recipientName="Nimal Perera", phone="0771000002", line1="12 Galle Road",
                                                                 city="Colombo", district="Colombo", postalCode="00300", isDefault=True)).raise_for_status()
    print("\nDone.  Customer: customer1@lankamart.local / " + PASSWORD + "   Seller: seller1@lankamart.local / " + PASSWORD)


if __name__ == "__main__":
    main()
