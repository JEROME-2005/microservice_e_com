# Development environment

Recorded 2026-11-11 on Windows 11 (build guide §0.2).

```
dotnet --version        10.0.403
docker --version        Docker version 29.8.0, build 88096ef
docker compose version  5.5.1
node --version          v24.16.0
npm --version           11.13.0
flutter --version       Flutter 3.47.6 • channel stable • https://github.com/flutter/flutter.git
python --version        Python 3.10.7
git --version           git version 2.54.0.windows.1
Aspire AppHost SDK      13.6.5
```

## Notes

- A native PostgreSQL already listens on port 5432, so LankaMart's Postgres is published on **5433**.
- Python 3.10 is enough for `scripts/seed_demo.py`; the Python recommender (§1.15) needs 3.12+.
- `dotnet-ef` is installed as a global tool (`dotnet tool install --global dotnet-ef --version "10.*"`).
