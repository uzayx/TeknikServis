# Teknik Servis API — Uçtan Uca Test Senaryoları

> Bölüm 1 & 2 kapanış testi. Otomatik koşum için: `scripts/api-smoke-test.ps1`
> Base URL: `http://localhost:5062`. Aşağıdaki senaryolar Swagger üzerinden elle de uygulanabilir.
> `{{musteriId}}` gibi ifadeler, önceki adımlarda dönen id değerlerini temsil eder.

---

## HAZIRLIK: Test Verisi

### H1 — Müşteri 1
`POST /api/customers`
```json
{
  "firstName": "Ahmet",
  "lastName": "Yılmaz",
  "email": "ahmet.yilmaz@example.com",
  "phone": "05551112233",
  "address": "Kadıköy, İstanbul"
}
```
**Beklenen:** `201 Created`. `firstName`/`lastName` ayrı alanlar, `fullName` hesaplanmış alan. → `{{musteriId}}`

### H2 — Müşteri 2 (aynı isimli farklı kişi)
Aynı isim, farklı e-posta ile ikinci kayıt.
**Beklenen:** `201`. Kişiler `Id` ve `Email` ile ayrışır; isim tekil değildir.

### H3 — Teknisyen 1
`POST /api/technicians`
```json
{
  "firstName": "Can",
  "lastName": "Demir",
  "email": "can.demir@teknikservis.com",
  "phone": "05554445566",
  "specialty": "Kombi/Doğalgaz"
}
```
**Beklenen:** `201`, `isActive: true`. → `{{teknisyen1Id}}`

### H4 — Teknisyen 2 (yeniden atama testi için)
Farklı isim/e-posta ile ikinci teknisyen. → `{{teknisyen2Id}}`

---

## SENARYO A — Mutlu Yol: Tam Yaşam Döngüsü

### A1 — Ticket aç
`POST /api/tickets`
```json
{
  "customerId": "{{musteriId}}",
  "title": "Kombi ısıtmıyor",
  "description": "Kombi çalışıyor fakat petekler soğuk kalıyor.",
  "priority": "High"
}
```
**Beklenen:**
- `201 Created`, `status: "New"`, `allowedNextStatuses: ["Assigned"]`
- `ticketNumber` → `TS-YYYYMMDD-XXXXXX`
- `slaDeadline` = `createdAt` + **8 saat** (High, appsettings'ten okunur)
- `statusHistories`: 1 kayıt (`null → New`)

### A2 — Teknisyen ata
`POST /api/tickets/{{ticketId}}/assign`
```json
{
  "technicianId": "{{teknisyen1Id}}",
  "changedByType": "Center",
  "note": "İlk atama yapıldı."
}
```
**Beklenen:** `200`. `status` otomatik `Assigned`, `assignedAt` dolu. History'de `previousTechnicianId: null`, `newTechnicianId: {{teknisyen1Id}}`.

### A3 — Teknisyen değişikliği (history kanıtı)
Aynı endpoint, `{{teknisyen2Id}}` ile.
**Beklenen:** History'de `previousTechnicianId: {{teknisyen1Id}}`, `newTechnicianId: {{teknisyen2Id}}`.
**Case'in "teknisyen değişiklikleri history olarak saklanmalı" şartının birebir kanıtı.**

### A4–A7 — Durum akışı
`POST /api/tickets/{{ticketId}}/status` — sırasıyla `InProgress` → `Completed` → `Approved` → `Closed`.
**Beklenen:** Her adımda `200`, `allowedNextStatuses` daralır. `Completed` → `completedAt` dolar, `Closed` → `closedAt` dolar ve `allowedNextStatuses: []`. Toplam **7 history kaydı**.

---

## SENARYO B — Kural İhlalleri

> Bölüm 2 puanının kalbi. Her ihlal doğru HTTP kodu + `errorCode` ile reddedilmeli.

| # | Test | Beklenen |
|---|---|---|
| B1 | Closed kaydı düzenleme (`PUT`) | `409` / `TICKET_LOCKED` |
| B2 | Closed kayda teknisyen atama | `409` / `TICKET_LOCKED` |
| B3 | Durum atlama (`New → InProgress`) | `409` / `INVALID_STATUS_TRANSITION` |
| B4 | Geriye dönüş (`Assigned → New`) | `409` / `INVALID_STATUS_TRANSITION` |
| B5 | Teknisyensiz `Assigned` | `409` / `TECHNICIAN_REQUIRED` |
| B6 | Aynı teknisyeni tekrar atama | `409` / `TECHNICIAN_ALREADY_ASSIGNED` |
| B7 | Boş `title`/`description` | `400` + ValidationProblemDetails |
| B8 | Geçersiz `changedByType` ("Robot") | `400` |
| B9 | Olmayan ticket `GET` | `404` / `NOT_FOUND` |
| B10 | Olmayan müşteri ile ticket açma | `404` / `NOT_FOUND` |
| B11 | Duplicate e-posta | `409` / `DATA_INTEGRITY_VIOLATION` |

Tüm hata yanıtları ProblemDetails formatında olmalı: `title`, `status`, `detail`, `instance`, `errorCode`, `traceId`.

### B12 — Pasif teknisyen ataması
> API'de deactivate endpoint'i bilinçli olarak yok (kapsam dışı). Bu kural `TicketService` birim testiyle (`AssignTechnician_InactiveTechnician_ShouldThrow`) doğrulanmıştır. Elle test için pgAdmin'de:
> ```sql
> UPDATE technicians SET "IsActive" = false WHERE "Email" = '...';
> ```
> Sonra atama denemesi → `409` / `TECHNICIAN_INACTIVE`. Test sonrası `true` yapılmalı.

---

## SENARYO C — Liste: Sayfalama / Filtreleme / Sıralama

| # | İstek | Beklenen |
|---|---|---|
| C1 | `?page=1&pageSize=3` | En fazla 3 kayıt; `totalCount`, `totalPages`, `hasNextPage` doğru |
| C2 | `?status=Closed` | Sadece Closed kayıtlar |
| C3 | `?priority=High&sortBy=createdAt&sortDir=asc` | Sadece High, eskiden yeniye |
| C4 | `?sortBy=slaDeadline&sortDir=asc` | SLA'sı en yakın (en acil) üstte |
| C5 | `?search=kombi` | Başlık/ticketNumber içinde arama, büyük-küçük harf duyarsız |
| C6 | `?status=New&priority=High&sortBy=slaDeadline&sortDir=asc&page=1&pageSize=5` | Tüm filtreler birlikte |
| C7 | `?page=0&pageSize=5000` | Hata YOK — `page`→1, `pageSize`→100 (guard) |

---

## Otomatik Koşum

```powershell
# Terminal 1
dotnet run --project TeknikServis.Api

# Terminal 2
.\scripts\api-smoke-test.ps1
```

Beklenen çıktı: **GECEN: 37 / KALAN: 0**

Birim testleri:
```powershell
dotnet test TeknikServis.Tests
```
Beklenen: **35/35**
