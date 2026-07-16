# Teknik Servis Yönetim Sistemi

.NET 9 + PostgreSQL 16 + React Native (Expo) + Docker

---

## Hızlı Başlangıç

### Gereksinimler
Docker Desktop. (Mobil için ek: Node.js + telefonda Expo Go)

### 1. Sistemi ayağa kaldır
```bash
docker compose up -d --build
```
Üç servis kalkar: PostgreSQL (5433), API (5062), pgAdmin (5050).
API, PostgreSQL sağlıklı olana kadar bekler ve ilk açılışta şemayı otomatik kurar.

> **Not:** Şema kurulur ama **veri gelmez** — bir sonraki adım şart.

### 2. Test verisi bas (~4.000 kayıt)
```powershell
Get-Content "sql/01-seed.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db
Get-Content "sql/04-indexes.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db
```
Linux/macOS için: `cat sql/01-seed.sql | docker exec -i ...`

Üretilenler: 4.000 ticket, ~13.600 durum geçmişi, ~5.800 yorum, ~2.800 ek.
`setseed(0.42)` sayesinde dağılımlar her çalıştırmada aynı — `sql/03-indexes.md`'deki ölçümler yeniden üretilebilir.

### 3. Doğrula
| Ne | Nerede |
|---|---|
| Swagger | http://localhost:5062/swagger |
| Sağlık kontrolü | http://localhost:5062/health/db |
| pgAdmin | http://localhost:5050 — `admin@teknikservis.com` / `admin123`, sunucu host: `postgres` |

```powershell
# Uctan uca test (50 senaryo, calisan container'a karsi)
.\scripts\api-smoke-test.ps1

# Birim testler (53 test)
dotnet test TeknikServis.Tests
```

### 4. Raporları çalıştır
```powershell
Get-Content "sql/02-reports.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db -P pager=off
```
Detay ve çalıştırma sırası: [`sql/README.md`](sql/README.md)

### 5. Mobil uygulama (opsiyonel)
```powershell
cd mobile
npm install
npx expo start
```
Telefonda **Expo Go** ile QR'ı okut.

> ⚠️ **Önce `mobile/src/config.js` içindeki `API_URL`'i kendi yerel IP'nizle güncelleyin.**
> Telefon `localhost`'u kendisi olarak görür. IP'nizi öğrenmek için: `ipconfig` (Windows) / `ifconfig` (macOS/Linux).
> Telefon ve bilgisayar aynı ağda olmalı; Windows Firewall 5062 portuna izin vermeli.

### Sıfırdan başlamak
```bash
docker compose down -v    # volume dahil her sey silinir
```

### Kullanılan portlar
5062 (API), 5433 (PostgreSQL), 5050 (pgAdmin). Doluysa `docker-compose.yml`'den değiştirin.
