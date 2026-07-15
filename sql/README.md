# SQL Scriptleri

Bu klasör, Bölüm 3 (PostgreSQL) teslimatını içerir.

## Çalıştırma sırası

Önkoşul: `docker compose up -d` ile PostgreSQL ayakta ve
`dotnet ef database update` ile şema oluşturulmuş olmalı.

| # | Dosya | Ne yapar |
|---|---|---|
| 1 | `01-seed.sql` | ⚠️ Veritabanını **temizler** ve sentetik veri basar |
| 2 | `04-indexes.sql` | Önerilen index'leri oluşturur + `VACUUM ANALYZE` |
| 3 | `02-reports.sql` | 6 raporu çalıştırır (salt okunur) |
| — | `03-indexes.md` | Index önerilerinin gerekçeleri ve EXPLAIN ANALYZE ölçümleri |

```powershell
# 1) Seed
Get-Content "sql/01-seed.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db

# 2) Index'ler
Get-Content "sql/04-indexes.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db

# 3) Raporlar
Get-Content "sql/02-reports.sql" -Raw | docker exec -i teknikservis-postgres psql -U teknikservis -d teknikservis_db -P pager=off
```

> Alternatif: pgAdmin (`localhost:5050`, host: `postgres`). Raporları oradan çalıştırırken
> sorguları **tek tek** seçip F5 yapın — pgAdmin yalnızca son SELECT'in sonucunu gösterir.

## Üretilen veri

| Tablo | Adet |
|---|---|
| technicians | 15 (1 pasif) |
| customers | 200 |
| service_tickets | 4.000 |
| ticket_status_histories | ~13.600 |
| comments | ~5.800 |
| attachments | ~2.800 |

Dağılım tasarımı:
- **Durum piramidi:** açık kayıtlar ağırlıklı (~%18 New, ~%19 Assigned, ~%20 InProgress),
  kapalıya doğru azalan — gerçek bir servis kuyruğunun şekli.
- **Çarpık teknisyen yükü:** `power(random(), 2.2)` ile birkaç teknisyende yığılma —
  `RANK()` ve `PERCENT_RANK()` sorgularının anlamlı çıkması için.
- **SLA ihlalleri:** ~%37 oranında, doğal olarak (kasıtlı damgalama yok). Gecikmeler
  SLA süresine orantılı üretilir; `r*r` dağılımı uzun kuyruk yaratır.
- **Kayıt yaşı duruma bağlı:** açık kayıtlar taze (ort. 19-32 saat), kapalı kayıtlar
  son 6 aya yayılmış.
- **Teknisyen değişikliği:** kayıtların ~%15'inde — history izinin test edilebilmesi için.

## Tekrarlanabilirlik

`SELECT setseed(0.42);` sayesinde `random()` her çalıştırmada aynı diziyi üretir;
`03-indexes.md` içindeki ölçümler yeniden üretilebilir.

`gen_random_uuid()` ayrı bir RNG kaynağı kullandığı için Id değerleri her çalıştırmada
farklıdır — dağılımlar (durum, öncelik, teknisyen yükü) aynı kalır.

## Notlar

- Script, uygulamanın iş kurallarını bilinçli olarak **bypass eder** (doğrudan INSERT).
  Zaman damgaları elle tutarlı üretilir: `created → assigned → completed → closed`.
  Amaç, window function ve `EXPLAIN ANALYZE` çalışmalarının anlamlı olacağı hacim ve dağılım.
- Ticket numaraları seed'de sıra numarasından üretilir (`TS-YYYYMMDD-000001`).
  Uygulama ise rastgele son ek kullanır; seed milisaniyede binlerce kayıt bastığı için
  rastgele 6 hane çakışabilirdi.
- Tablo adları `snake_case`, kolon adları `PascalCase` (`"Status"`, `"CreatedAt"`).
  EF Core varsayılanı korunduğu için ham SQL'de kolonlar çift tırnak ister.
