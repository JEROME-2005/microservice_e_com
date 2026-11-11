#!/usr/bin/env python3
"""End-to-end checks for the Phase 1 buying loop (build guide §1.11 check 5 and §1.12 checks 1-8, 10).

    python scripts/e2e_checkout.py          # backend running in Development + scripts/seed_demo.py already run

Auth calls go straight to Identity (:5101) so the gateway's 10/min auth limit isn't spent on setup;
every feature call goes through the gateway (:5100). Check 8 (timeout) backdates the order's payment window in
Postgres via `docker exec` instead of waiting 15 minutes, then waits for the 30 s expiry sweeper.
"""
import concurrent.futures as cf
import os
import random
import subprocess
import sys
import time
import urllib.parse
import uuid

import requests

G = "http://localhost:5100/api/v1"
ID = "http://localhost:5101/api/v1"
PW = "Sup3rSecret!!"
ADDR = {"deliveryAddress": {"recipientName": "Nimal Perera", "phone": "0771234567", "line1": "12 Galle Road", "line2": None,
                            "city": "Colombo", "district": "Colombo", "postalCode": "00300"}}
CARD_OK = {"cardNumber": "4111 1111 1111 1111", "expMonth": 12, "expYear": 2030, "cvc": "123", "cardHolder": "N PERERA"}
CARD_DECLINE = dict(CARD_OK, cardNumber="4000000000000002")
PG_CONTAINER = os.environ.get("PG_CONTAINER", "")
failures = []


def check(name, ok, detail=""):
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}{(' - ' + str(detail)) if detail else ''}")
    if not ok:
        failures.append(name)


def h(token=None, **extra):
    d = {"Authorization": f"Bearer {token}"} if token else {}
    d.update(extra)
    return d


def sql(query):
    name = PG_CONTAINER or subprocess.run(["docker", "ps", "--format", "{{.Names}}"], capture_output=True, text=True).stdout.split()
    if isinstance(name, list):
        name = next(n for n in name if n.startswith("postgres") or n == "lm-postgres")
    out = subprocess.run(["docker", "exec", "-e", "PGPASSWORD=postgres_dev_pw", name, "psql", "-h", "localhost", "-U", "postgres", "-d", "lankamart",
                          "-tA", "-c", query], capture_output=True, text=True)
    return out.stdout.strip()


def new_customer():
    n = random.randint(1000000, 9999999)
    email, phone = f"e2e{n}@example.com", f"076{n}"
    requests.post(ID + "/auth/register", json=dict(email=email, phone=phone, password=PW, fullName="E2E Buyer", role="customer")).raise_for_status()
    for ch, dest in (("email", email), ("phone", "+94" + phone[1:])):
        code = requests.get(ID + "/dev/otp/" + urllib.parse.quote(dest, safe="")).json()["code"]
        requests.post(ID + "/auth/verify-otp", json=dict(email=email, channel=ch, code=code)).raise_for_status()
    tok = requests.post(ID + "/auth/login", json=dict(email=email, password=PW)).json()["accessToken"]
    for _ in range(20):                                   # profile arrives via the UserRegistered event
        if requests.get(G + "/customers/me", headers=h(tok)).status_code == 200:
            break
        time.sleep(0.5)
    return tok


def variant(query):
    slug = requests.get(G + "/products", params={"query": query}).json()["items"][0]["slug"]
    return requests.get(G + "/products/" + slug).json()["variants"][0]["id"]


def stock(v):
    return sql(f"select qty_on_hand||'/'||qty_reserved from inventory.inventory_items where variant_id='{v}'")


def place(tok, v, qty=1, key=None):
    requests.post(G + "/cart/items", headers=h(tok), json={"variantId": v, "quantity": qty}).raise_for_status()
    return requests.post(G + "/orders", headers=h(tok, **{"Idempotency-Key": key or str(uuid.uuid4())}), json=ADDR)


def wait_status(tok, order_id, want, seconds=20):
    st = None
    for _ in range(seconds * 2):
        st = requests.get(f"{G}/orders/{order_id}", headers=h(tok)).json()["status"]
        if st == want:
            break
        time.sleep(0.5)
    return st


def main():
    v = variant("Yoga Mat")
    print("1-2 happy path")
    tok = new_customer()
    before = stock(v)
    r = place(tok, v, 2)
    check("POST /orders -> 201", r.status_code == 201, r.status_code)
    o = r.json()
    check("order PendingPayment + stock reserved", requests.get(f"{G}/orders/{o['orderId']}", headers=h(tok)).json()["status"] == "PendingPayment"
          and stock(v).split("/")[1] != before.split("/")[1], stock(v))
    r = requests.post(f"{G}/payments/{o['paymentId']}/pay", headers=h(tok), json=CARD_OK)
    check("pay 4111 -> 202", r.status_code == 202, r.status_code)
    r2 = requests.post(f"{G}/payments/{o['paymentId']}/pay", headers=h(tok), json=CARD_OK)
    check("5 double-click pay -> 409 PAYMENT_IN_PROGRESS", r2.status_code == 409 and "PAYMENT_IN_PROGRESS" in r2.text, r2.text[:80])
    check("order becomes Paid", wait_status(tok, o["orderId"], "Paid") == "Paid")
    time.sleep(1.5)
    on_b, res_b = map(int, before.split("/"))
    on_a, res_a = map(int, stock(v).split("/"))
    check("stock: on_hand -2, reserved back", on_a == on_b - 2 and res_a == res_b, f"{before} -> {stock(v)}")
    ledger = sql(f"select count(*)||' '||sum(debit_cents)||' '||sum(credit_cents) from payments.ledger_entries e join payments.ledger_transactions t on t.id=e.transaction_id where t.order_id='{o['orderId']}'")
    cnt, deb, cred = ledger.split()
    check("ledger: 2 rows, balanced, = total", cnt == "2" and deb == cred == str(o["totalCents"]), ledger)
    check("ledger globally balanced", sql("select count(*) from (select transaction_id from payments.ledger_entries group by transaction_id having sum(debit_cents)<>sum(credit_cents)) x") == "0")
    tl = requests.get(f"{G}/orders/{o['orderId']}/tracking", headers=h(tok)).json()["timeline"]
    check("tracking timeline PendingPayment -> Paid", [e["status"] for e in tl] == ["PendingPayment", "Paid"], [e["status"] for e in tl])
    check("card number never logged", "4111 1111" not in open(".run/apphost.log", encoding="utf-8", errors="ignore").read())

    print("6-7 failure lab")
    r = requests.post(f"{G}/dev/mock/{o['paymentId']}/duplicate-webhook")
    check("duplicate webhook -> 200 & no second ledger tx", r.ok and r.json()["httpStatus"] == 200
          and sql(f"select count(*) from payments.ledger_transactions where order_id='{o['orderId']}'") == "1", r.text)
    r = requests.post(f"{G}/dev/mock/{o['paymentId']}/bad-signature")
    check("bad signature -> 401", r.ok and r.json()["httpStatus"] == 401, r.text)
    r = requests.post(f"{G}/dev/mock/{o['paymentId']}/amount-mismatch")
    check("amount mismatch -> 422", r.ok and r.json()["httpStatus"] == 422, r.text)

    print("10 payment IDOR")
    other = new_customer()
    check("other user GET /payments/{id} -> 404", requests.get(f"{G}/payments/{o['paymentId']}", headers=h(other)).status_code == 404)
    check("other user pay -> 404", requests.post(f"{G}/payments/{o['paymentId']}/pay", headers=h(other), json=CARD_OK).status_code == 404)
    check("dev advance as customer -> 403", requests.post(f"{G}/dev/orders/{o['orderId']}/advance", headers=h(tok)).status_code == 403)
    admin = requests.post(ID + "/auth/login", json=dict(email="admin@lankamart.local", password="Admin#12345678")).json()["accessToken"]
    steps = [requests.post(f"{G}/dev/orders/{o['orderId']}/advance", headers=h(admin)).json().get("status") for _ in range(7)]
    check("dev simulator advances to Completed", steps[-1] == "Completed", steps)

    print("3 decline")
    tok = new_customer()
    before = stock(v)
    o = place(tok, v, 1).json()
    requests.post(f"{G}/payments/{o['paymentId']}/pay", headers=h(tok), json=CARD_DECLINE)
    check("order PaymentFailed", wait_status(tok, o["orderId"], "PaymentFailed") == "PaymentFailed")
    time.sleep(1.5)
    check("stock restored", stock(v) == before, f"{before} -> {stock(v)}")
    cart = requests.get(G + "/cart", headers=h(tok)).json()["lines"]
    check("cart restored", len(cart) == 1 and cart[0]["quantity"] == 1, cart)
    requests.delete(f"{G}/cart/items/{v}", headers=h(tok))

    print("1.11-5 idempotency")
    tok = new_customer()
    key = str(uuid.uuid4())
    a = place(tok, v, 1, key).json()
    b = requests.post(G + "/orders", headers=h(tok, **{"Idempotency-Key": key}), json=ADDR).json()
    check("same key twice -> same order", a["orderId"] == b["orderId"], (a.get("orderId"), b.get("orderId")))
    requests.post(G + "/cart/items", headers=h(tok), json={"variantId": v, "quantity": 1})
    key2 = str(uuid.uuid4())
    with cf.ThreadPoolExecutor(5) as ex:
        rs = list(ex.map(lambda _: requests.post(G + "/orders", headers=h(tok, **{"Idempotency-Key": key2}), json=ADDR), range(5)))
    created = {r.json()["orderId"] for r in rs if r.status_code == 201}
    check("same key 5x concurrently -> one order", len(created) == 1, [r.status_code for r in rs])
    uid = requests.get(ID + "/auth/me", headers=h(tok)).json()["userId"]
    check("customer has exactly 2 orders", sql(f"select count(*) from orders.orders where customer_id='{uid}'") == "2")

    print("8 timeout / expiry")
    before = stock(v)
    o = place(tok, v, 1).json()
    sql(f"update orders.orders set payment_expires_at = now() - interval '1 minute' where id='{o['orderId']}'")
    st = wait_status(tok, o["orderId"], "Expired", seconds=45)
    check("unpaid order -> Expired", st == "Expired", st)
    time.sleep(1.5)
    check("stock released after expiry", stock(v) == before, f"{before} -> {stock(v)}")

    print("cancel while unpaid")
    o = place(tok, v, 1).json()
    check("customer cancel -> 204", requests.post(f"{G}/orders/{o['orderId']}/cancel", headers=h(tok)).status_code == 204)
    check("cancel again -> 422 CANNOT_CANCEL", "CANNOT_CANCEL" in requests.post(f"{G}/orders/{o['orderId']}/cancel", headers=h(tok)).text)
    # paying AFTER the order was cancelled: money is taken but there is no order → OrderPaymentOrphaned → payment flagged for refund
    requests.post(f"{G}/payments/{o['paymentId']}/pay", headers=h(tok), json=CARD_OK)
    pst = None
    for _ in range(30):
        pst = requests.get(f"{G}/payments/{o['paymentId']}", headers=h(tok)).json()["status"]
        if pst == "RefundRequired":
            break
        time.sleep(0.5)
    check("paid after cancel -> payment RefundRequired (orphan flow)", pst == "RefundRequired", pst)

    print("\nRESULT:", "ALL PASSED" if not failures else f"{len(failures)} FAILED: {failures}")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
