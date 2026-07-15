-- ============================================================
-- 02-reports.sql — Raporlama sorgulari (CTE + Window Function + JOIN)
-- Durum kodlari: 0=New 1=Assigned 2=InProgress 3=Completed 4=Approved 5=Closed
-- Oncelik: 0=Low 1=Medium 2=High 3=Critical
-- ============================================================

-- ============================================================
-- SORGU 1: En yogun teknisyenler
-- Aktif is yuku (Assigned+InProgress) uzerinden RANK; PERCENT_RANK ile
-- teknisyenin takim icindeki goreli konumu.
-- Kullanilan: CTE, LEFT JOIN, FILTER, RANK, PERCENT_RANK
-- ============================================================
WITH is_yuku AS (
    SELECT t."Id",
           t."FirstName" || ' ' || t."LastName" AS teknisyen,
           t."Specialty" AS uzmanlik,
           COUNT(st."Id") FILTER (WHERE st."Status" IN (1,2)) AS aktif_is,
           COUNT(st."Id") FILTER (WHERE st."Status" = 3)      AS onay_bekleyen,
           COUNT(st."Id")                                     AS toplam_atanan
    FROM technicians t
    LEFT JOIN service_tickets st ON st."AssignedTechnicianId" = t."Id"
    WHERE t."IsActive" = TRUE
    GROUP BY t."Id", teknisyen, uzmanlik
)
SELECT teknisyen, uzmanlik, aktif_is, onay_bekleyen, toplam_atanan,
       RANK() OVER (ORDER BY aktif_is DESC) AS yogunluk_sirasi,
       ROUND((100 * PERCENT_RANK() OVER (ORDER BY aktif_is))::numeric, 1) AS yuzdelik_dilim
FROM is_yuku
ORDER BY yogunluk_sirasi, teknisyen;

-- ============================================================
-- SORGU 2A: SLA ihlalleri — detay listesi (en cok geciken 25 kayit)
-- Ihlal tanimi: tamamlanmis kayitlarda CompletedAt > SlaDeadline;
-- tamamlanmamis kayitlarda NOW() > SlaDeadline.
-- Kullanilan: CTE, JOIN, LEFT JOIN
-- ============================================================
WITH ihlal_analizi AS (
    SELECT st."Id", st."TicketNumber", st."Status", st."Priority",
           st."CustomerId", st."AssignedTechnicianId", st."SlaDeadline",
           CASE WHEN st."CompletedAt" IS NOT NULL
                THEN st."CompletedAt" > st."SlaDeadline"
                ELSE NOW() > st."SlaDeadline" END AS ihlal_var,
           CASE WHEN st."CompletedAt" IS NOT NULL
                THEN st."CompletedAt" - st."SlaDeadline"
                ELSE NOW() - st."SlaDeadline" END AS gecikme
    FROM service_tickets st
)
SELECT i."TicketNumber",
       c."FirstName" || ' ' || c."LastName" AS musteri,
       COALESCE(t."FirstName" || ' ' || t."LastName", '(atanmadi)') AS teknisyen,
       i."Priority" AS oncelik, i."Status" AS durum,
       i."SlaDeadline" AS sla_bitis,
       ROUND((EXTRACT(EPOCH FROM i.gecikme)/3600)::numeric, 1) AS gecikme_saat
FROM ihlal_analizi i
JOIN customers c ON c."Id" = i."CustomerId"
LEFT JOIN technicians t ON t."Id" = i."AssignedTechnicianId"
WHERE i.ihlal_var
ORDER BY i.gecikme DESC
LIMIT 25;

-- ============================================================
-- SORGU 2B: SLA ihlal orani — oncelik bazinda ozet
-- Window ile genel toplam icindeki pay.
-- Kullanilan: CTE, agregasyon, SUM() OVER ()
-- ============================================================
WITH ihlal_analizi AS (
    SELECT st."Priority",
           CASE WHEN st."CompletedAt" IS NOT NULL
                THEN st."CompletedAt" > st."SlaDeadline"
                ELSE NOW() > st."SlaDeadline" END AS ihlal_var
    FROM service_tickets st
)
SELECT "Priority" AS oncelik,
       COUNT(*) AS toplam_kayit,
       COUNT(*) FILTER (WHERE ihlal_var) AS ihlal_sayisi,
       ROUND(100.0 * COUNT(*) FILTER (WHERE ihlal_var) / COUNT(*), 1) AS ihlal_orani_yuzde,
       ROUND(100.0 * COUNT(*) FILTER (WHERE ihlal_var)
             / SUM(COUNT(*) FILTER (WHERE ihlal_var)) OVER (), 1) AS tum_ihlaller_icindeki_pay
FROM ihlal_analizi
GROUP BY "Priority"
ORDER BY "Priority" DESC;

-- ============================================================
-- SORGU 3: Bekleyen kayitlar (New + Assigned) — aciliyet siralamasi
-- SLA'ya kalan sureye gore siniflandirma; oncelik icinde sira numarasi.
-- Kullanilan: JOIN, LEFT JOIN, CASE, ROW_NUMBER (PARTITION BY)
-- ============================================================
SELECT st."TicketNumber",
       c."FirstName" || ' ' || c."LastName" AS musteri,
       COALESCE(t."FirstName" || ' ' || t."LastName", '(atanmadi)') AS teknisyen,
       st."Status" AS durum, st."Priority" AS oncelik,
       ROUND((EXTRACT(EPOCH FROM (NOW() - st."CreatedAt"))/3600)::numeric, 1) AS bekleme_saat,
       ROUND((EXTRACT(EPOCH FROM (st."SlaDeadline" - NOW()))/3600)::numeric, 1) AS sla_kalan_saat,
       CASE WHEN NOW() > st."SlaDeadline" THEN 'IHLAL'
            WHEN st."SlaDeadline" - NOW() < interval '4 hours' THEN 'KRITIK'
            WHEN st."SlaDeadline" - NOW() < interval '12 hours' THEN 'YAKLASTI'
            ELSE 'NORMAL' END AS aciliyet,
       ROW_NUMBER() OVER (PARTITION BY st."Priority" ORDER BY st."SlaDeadline") AS oncelik_ici_sira
FROM service_tickets st
JOIN customers c ON c."Id" = st."CustomerId"
LEFT JOIN technicians t ON t."Id" = st."AssignedTechnicianId"
WHERE st."Status" IN (0, 1)
ORDER BY st."SlaDeadline"
LIMIT 50;

-- ============================================================
-- SORGU 4: Teknisyen performans raporu — takim ortalamasina kiyas
-- Ortalama cozum suresi (AssignedAt -> CompletedAt), SLA uyum orani.
-- AVG(AVG(..)) OVER (): grup ortalamasinin takim genelindeki ortalamasi.
-- Kullanilan: CTE, JOIN, AVG() OVER (), DENSE_RANK
-- ============================================================
WITH tamamlanan AS (
    SELECT st."AssignedTechnicianId" AS tech_id,
           EXTRACT(EPOCH FROM (st."CompletedAt" - st."AssignedAt"))/3600 AS cozum_saat,
           (st."CompletedAt" <= st."SlaDeadline")::int AS sla_uyumlu
    FROM service_tickets st
    WHERE st."Status" >= 3
      AND st."CompletedAt" IS NOT NULL
      AND st."AssignedAt"  IS NOT NULL
)
SELECT t."FirstName" || ' ' || t."LastName" AS teknisyen,
       t."Specialty" AS uzmanlik,
       COUNT(*) AS tamamlanan_is,
       ROUND(AVG(x.cozum_saat)::numeric, 1) AS ort_cozum_saat,
       ROUND((AVG(AVG(x.cozum_saat)) OVER ())::numeric, 1) AS takim_ortalamasi,
       ROUND((AVG(x.cozum_saat) - AVG(AVG(x.cozum_saat)) OVER ())::numeric, 1) AS ortalamadan_sapma,
       ROUND(100.0 * AVG(x.sla_uyumlu)::numeric, 1) AS sla_uyum_yuzde,
       DENSE_RANK() OVER (ORDER BY AVG(x.cozum_saat)) AS hiz_sirasi
FROM tamamlanan x
JOIN technicians t ON t."Id" = x.tech_id
GROUP BY t."Id", teknisyen, uzmanlik
ORDER BY hiz_sirasi;

-- ============================================================
-- SORGU 5: Durum funnel'i — adimlar arasi gecis sureleri (darbogaz analizi)
-- History uzerinden LAG ile her gecisin bir onceki kayittan farki alinir;
-- durum bazinda ortalama/medyan/maksimum bekleme hesaplanir.
-- Teknisyen degisikligi satirlari (FromStatus NULL, ToStatus>0) haric tutulur.
-- Kullanilan: CTE, LAG (PARTITION BY), PERCENTILE_CONT
-- ============================================================
WITH gecisler AS (
    SELECT h."ServiceTicketId", h."ToStatus", h."ChangedAt",
           LAG(h."ChangedAt") OVER (
               PARTITION BY h."ServiceTicketId" ORDER BY h."ChangedAt"
           ) AS onceki_zaman
    FROM ticket_status_histories h
    WHERE h."FromStatus" IS NOT NULL OR h."ToStatus" = 0
), sureler AS (
    SELECT "ToStatus",
           EXTRACT(EPOCH FROM ("ChangedAt" - onceki_zaman))/3600 AS gecis_saat
    FROM gecisler
    WHERE onceki_zaman IS NOT NULL
)
SELECT "ToStatus" AS hedef_durum,
       CASE "ToStatus" WHEN 1 THEN 'Atama bekleme (New->Assigned)'
                       WHEN 2 THEN 'Ise baslama (Assigned->InProgress)'
                       WHEN 3 THEN 'Onarim suresi (InProgress->Completed)'
                       WHEN 4 THEN 'Onay bekleme (Completed->Approved)'
                       WHEN 5 THEN 'Kapanis (Approved->Closed)' END AS adim,
       COUNT(*) AS gecis_sayisi,
       ROUND(AVG(gecis_saat)::numeric, 1) AS ortalama_saat,
       ROUND((PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY gecis_saat))::numeric, 1) AS medyan_saat,
       ROUND(MAX(gecis_saat)::numeric, 1) AS maksimum_saat
FROM sureler
GROUP BY "ToStatus"
ORDER BY "ToStatus";

-- ============================================================
-- SORGU 6: Musteri etkilesim yogunlugu — en cok geri donus alan kayitlar
-- Yorum + ek sayilari; DENSE_RANK ile etkilesim siralamasi.
-- Not: comments ve attachments ayri CTE'lerde sayilir; ayni sorguda iki
-- LEFT JOIN + COUNT kartezyen sisirmesi yaratir (klasik hata).
-- Kullanilan: CTE x2, LEFT JOIN, DENSE_RANK
-- ============================================================
WITH yorum_sayisi AS (
    SELECT "ServiceTicketId", COUNT(*) AS yorum
    FROM comments GROUP BY "ServiceTicketId"
), ek_sayisi AS (
    SELECT "ServiceTicketId", COUNT(*) AS ek
    FROM attachments GROUP BY "ServiceTicketId"
)
SELECT st."TicketNumber",
       LEFT(st."Title", 40) AS baslik,
       c."FirstName" || ' ' || c."LastName" AS musteri,
       st."Status" AS durum,
       COALESCE(y.yorum, 0) AS yorum_sayisi,
       COALESCE(e.ek, 0) AS ek_sayisi,
       COALESCE(y.yorum, 0) + COALESCE(e.ek, 0) AS toplam_etkilesim,
       DENSE_RANK() OVER (ORDER BY COALESCE(y.yorum,0) + COALESCE(e.ek,0) DESC) AS etkilesim_sirasi
FROM service_tickets st
JOIN customers c ON c."Id" = st."CustomerId"
LEFT JOIN yorum_sayisi y ON y."ServiceTicketId" = st."Id"
LEFT JOIN ek_sayisi   e ON e."ServiceTicketId" = st."Id"
WHERE COALESCE(y.yorum, 0) + COALESCE(e.ek, 0) > 0
ORDER BY etkilesim_sirasi
LIMIT 20;
