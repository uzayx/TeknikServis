-- ============================================================
-- 04-indexes.sql — Onerilen index'ler
-- Gerekceler ve EXPLAIN ANALYZE olcumleri: sql/03-indexes.md
--
-- Kullanim: 01-seed.sql calistirildiktan sonra uygulanir.
-- Uretimde CONCURRENTLY ile olusturulmalidir (tablo kilitlenmesin);
-- bu script tek kullanicili gelistirme ortami icin yazilmistir.
-- ============================================================

-- ---------- Oneri 1: Partial index — aktif kayitlar ----------
-- Hedef: operasyon ekrani, "en acil aktif kayitlar" listesi.
-- Olculen kazanc: Seq Scan+Sort -> Index Scan, cost 287.20 -> 0.28 (startup),
-- buffers 144 -> 52, exec 0.874ms -> 0.166ms.
-- NOT: Window function iceren sorgularda etkisizdir (bkz. 03-indexes.md).
DROP INDEX IF EXISTS ix_tickets_active;
CREATE INDEX ix_tickets_active
ON service_tickets ("SlaDeadline")
WHERE "Status" NOT IN (4, 5);

-- ---------- Oneri 2: Covering index — SLA ihlal taramasi ----------
-- Hedef: Sorgu 2A / 2B.
-- Olculen kazanc: Seq Scan+Sort -> Index Only Scan (Heap Fetches: 0),
-- buffers 144 -> 3, exec 1.031ms -> 0.102ms.
DROP INDEX IF EXISTS ix_tickets_sla_covering;
CREATE INDEX ix_tickets_sla_covering
ON service_tickets ("SlaDeadline")
INCLUDE ("TicketNumber", "Status", "Priority", "CompletedAt");

-- ---------- Oneri 3: Covering partial index — teknisyen performansi ----------
-- Hedef: Sorgu 4.
-- INCLUDE("AssignedAt") kritik: bu kolon olmadan planlayici index'i yok sayiyor,
-- cunku sorgu CompletedAt - AssignedAt hesapliyor ve heap'e gitmek zorunda kaliyor.
-- Olculen kazanc: Seq Scan+HashAggregate -> Index Only Scan+GroupAggregate,
-- cost 203.05 -> 88.55, buffers 141 -> 13.
DROP INDEX IF EXISTS ix_tickets_tech_completed;
CREATE INDEX ix_tickets_tech_completed
ON service_tickets ("AssignedTechnicianId", "CompletedAt")
INCLUDE ("AssignedAt")
WHERE "CompletedAt" IS NOT NULL AND "AssignedAt" IS NOT NULL;

-- Index Only Scan icin gorunurluk haritasinin guncel olmasi sart:
VACUUM ANALYZE service_tickets;

-- ---------- Dogrulama ----------
SELECT indexname, pg_size_pretty(pg_relation_size(indexname::regclass)) AS boyut
FROM pg_indexes
WHERE tablename = 'service_tickets'
ORDER BY pg_relation_size(indexname::regclass) DESC;
