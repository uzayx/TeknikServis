-- ============================================================
-- 01-seed.sql — Sentetik veri ureteci (raporlama & index analizi icin)
--
-- NOT: Bu script veritabanini TEMIZLER ve yeniden doldurur.
-- Uygulamanin is kurallarini bilincli olarak bypass eder; zaman
-- damgalari (created -> assigned -> completed -> closed) elle
-- tutarli uretilir. Amac: window function ve EXPLAIN ANALYZE
-- calismalarinin anlamli olacagi hacim ve dagilimda veri.
--
-- Icerik: 15 teknisyen (1 pasif), 200 musteri, 4000 ticket,
-- ~9000 history, ~4000 yorum, ~1500 ek metadata.
-- Dagilim: durum piramidi (cok New/Assigned, az Closed),
-- teknisyen yuku carpik (RANK anlamli olsun), dogal SLA ihlalleri.
-- ============================================================

BEGIN;

TRUNCATE TABLE ticket_status_histories, comments, attachments,
               service_tickets, customers, technicians CASCADE;

-- ---------- 1) Teknisyenler ----------
INSERT INTO technicians ("Id","FirstName","LastName","Email","Phone","Specialty","IsActive","CreatedAt") VALUES
(gen_random_uuid(),'Can','Demir','can.demir@teknikservis.com','05550000001','Kombi/Dogalgaz',TRUE,NOW()-interval '400 days'),
(gen_random_uuid(),'Elif','Kaya','elif.kaya@teknikservis.com','05550000002','Elektrik',TRUE,NOW()-interval '390 days'),
(gen_random_uuid(),'Murat','Sahin','murat.sahin@teknikservis.com','05550000003','Klima',TRUE,NOW()-interval '380 days'),
(gen_random_uuid(),'Zeynep','Celik','zeynep.celik@teknikservis.com','05550000004','Beyaz Esya',TRUE,NOW()-interval '370 days'),
(gen_random_uuid(),'Emre','Yildiz','emre.yildiz@teknikservis.com','05550000005','Su Tesisati',TRUE,NOW()-interval '360 days'),
(gen_random_uuid(),'Selin','Arslan','selin.arslan@teknikservis.com','05550000006','Kombi/Dogalgaz',TRUE,NOW()-interval '350 days'),
(gen_random_uuid(),'Burak','Dogan','burak.dogan@teknikservis.com','05550000007','Klima',TRUE,NOW()-interval '340 days'),
(gen_random_uuid(),'Aylin','Kilic','aylin.kilic@teknikservis.com','05550000008','Elektrik',TRUE,NOW()-interval '330 days'),
(gen_random_uuid(),'Kerem','Aydin','kerem.aydin@teknikservis.com','05550000009','Beyaz Esya',TRUE,NOW()-interval '320 days'),
(gen_random_uuid(),'Deniz','Ozturk','deniz.ozturk@teknikservis.com','05550000010','Su Tesisati',TRUE,NOW()-interval '310 days'),
(gen_random_uuid(),'Gizem','Aksoy','gizem.aksoy@teknikservis.com','05550000011','Kombi/Dogalgaz',TRUE,NOW()-interval '300 days'),
(gen_random_uuid(),'Onur','Polat','onur.polat@teknikservis.com','05550000012','Klima',TRUE,NOW()-interval '290 days'),
(gen_random_uuid(),'Merve','Erdem','merve.erdem@teknikservis.com','05550000013','Elektrik',TRUE,NOW()-interval '280 days'),
(gen_random_uuid(),'Tolga','Kurt','tolga.kurt@teknikservis.com','05550000014','Beyaz Esya',TRUE,NOW()-interval '270 days'),
(gen_random_uuid(),'Hakan','Simsek','hakan.simsek@teknikservis.com','05550000015','Su Tesisati',FALSE,NOW()-interval '260 days');

-- ---------- 2) Musteriler (200) ----------
INSERT INTO customers ("Id","FirstName","LastName","Email","Phone","Address","CreatedAt")
SELECT gen_random_uuid(),
  (ARRAY['Ali','Ayse','Mehmet','Fatma','Ahmet','Emine','Mustafa','Hatice','Huseyin','Zehra',
         'Ibrahim','Elif','Osman','Meryem','Yusuf','Sultan','Ramazan','Hanife','Halil','Esra'])[1+(i%20)],
  (ARRAY['Yilmaz','Kaya','Demir','Sahin','Celik','Yildiz','Aydin','Ozturk','Arslan','Dogan',
         'Kilic','Aslan','Cetin','Kara','Koc','Kurt','Ozkan','Simsek','Polat','Erdem'])[1+((i/20)%20)],
  'musteri'||i||'@ornek.com',
  '0532'||lpad(i::text,7,'0'),
  (ARRAY['Kadikoy, Istanbul','Besiktas, Istanbul','Cankaya, Ankara','Konak, Izmir','Nilufer, Bursa',
         'Muratpasa, Antalya','Selcuklu, Konya','Sisli, Istanbul','Kecioren, Ankara','Bornova, Izmir'])[1+(i%10)],
  NOW() - (random()*300)::int * interval '1 day'
FROM generate_series(1,200) i;

-- ---------- 3) Yardimci gecici tablolar ----------
CREATE TEMP TABLE tmp_tech AS
SELECT "Id" AS tech_id, ROW_NUMBER() OVER (ORDER BY "Email") AS rn
FROM technicians WHERE "IsActive" = TRUE;  -- 14 aktif teknisyen

CREATE TEMP TABLE tmp_cust AS
SELECT "Id" AS cust_id, ROW_NUMBER() OVER (ORDER BY "Email") AS rn
FROM customers;

-- ---------- 4) Ticket iskeleti ----------
-- power(random(),2.2): dusuk rn'li teknisyenlere yigilma -> RANK anlamli olur.
-- Durum piramidi: 18% New, 19% Assigned, 19% InProgress, 17% Completed, 13% Approved, 14% Closed.
CREATE TEMP TABLE seed AS
WITH b AS (
  SELECT i,
    gen_random_uuid() AS id,
    NOW() - (random()*180)::int * interval '1 day'
          - (random()*86400)::int * interval '1 second' AS raw_created,
    floor(random()*4)::int AS priority,           -- 0..3
    random() AS s_pick,
    1 + floor(power(random(),2.2)*14)::int AS final_rn,
    random() < 0.15 AS has_change,                -- %15 teknisyen degisikligi
    (random()*random()*24)::numeric  AS d1_h,     -- atama gecikmesi (saat)
    (random()*random()*36)::numeric  AS d2_h,     -- ise baslama gecikmesi
    (2 + random()*random()*90)::numeric AS d3_h,  -- is suresi (SLA ihlalleri dogal olarak buradan cikar)
    (random()*48)::numeric AS d4_h,               -- onay gecikmesi
    (random()*24)::numeric AS d5_h                -- kapanis gecikmesi
  FROM generate_series(1,4000) i
), b2 AS (
  SELECT b.*,
    CASE WHEN s_pick<0.18 THEN 0 WHEN s_pick<0.37 THEN 1 WHEN s_pick<0.56 THEN 2
         WHEN s_pick<0.73 THEN 3 WHEN s_pick<0.86 THEN 4 ELSE 5 END AS status,
    CASE priority WHEN 3 THEN 4 WHEN 2 THEN 8 WHEN 1 THEN 24 ELSE 72 END AS sla_h,
    1 + floor(random()*200)::int AS cust_rn,
    LEAST(final_rn, 14) AS f_rn
  FROM b
), b3 AS (
  SELECT b2.*,
    CASE WHEN has_change AND status>=1 THEN (f_rn % 14) + 1 ELSE f_rn END AS init_rn,
    -- Ilerlemis kayitlarin zaman zinciri (maks ~9 gun) gelecege tasmasin:
    CASE WHEN status = 0 THEN raw_created
         ELSE LEAST(raw_created, NOW() - interval '10 days') END AS created_at
  FROM b2
)
SELECT b3.*,
  'TS-'||to_char(created_at,'YYYYMMDD')||'-'||lpad(i::text,6,'0') AS ticket_no,
  created_at + d1_h                     * interval '1 hour' AS t_assign,
  created_at + (d1_h+d2_h)              * interval '1 hour' AS t_prog,
  created_at + (d1_h+d2_h+d3_h)         * interval '1 hour' AS t_comp,
  created_at + (d1_h+d2_h+d3_h+d4_h)    * interval '1 hour' AS t_appr,
  created_at + (d1_h+d2_h+d3_h+d4_h+d5_h)* interval '1 hour' AS t_close
FROM b3;

-- ---------- 5) service_tickets ----------
INSERT INTO service_tickets
  ("Id","TicketNumber","CustomerId","AssignedTechnicianId","Title","Description",
   "Status","Priority","SlaDeadline","CreatedAt","AssignedAt","CompletedAt","ClosedAt")
SELECT s.id, s.ticket_no, c.cust_id,
  CASE WHEN s.status>=1 THEN ft.tech_id END,
  (ARRAY['Kombi isitmiyor','Klima sogutmuyor','Priz calismiyor','Musluk damlatiyor',
         'Buzdolabi ses yapiyor','Kombi ariza kodu veriyor','Klima su akitiyor',
         'Sigorta atiyor','Camasir makinesi calismiyor','Petek sogugu'])[1+(s.i%10)]
    || ' - ' || s.ticket_no,
  'Musteri bildirimi: cihazda ariza tespit edildi, servis talebi olusturuldu. Kayit no: '||s.ticket_no,
  s.status, s.priority,
  s.created_at + s.sla_h * interval '1 hour',
  s.created_at,
  CASE WHEN s.status>=1 THEN s.t_assign END,
  CASE WHEN s.status>=3 THEN s.t_comp END,
  CASE WHEN s.status=5  THEN s.t_close END
FROM seed s
JOIN tmp_cust c ON c.rn = s.cust_rn
LEFT JOIN tmp_tech ft ON ft.rn = s.f_rn;

-- ---------- 6) Durum gecmisi ----------
-- Olusturma kaydi (tum ticket'lar)
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), id, NULL, 0, NULL, NULL, 'Customer', 'Ariza kaydi olusturuldu.', created_at
FROM seed;

-- Ilk atama: New -> Assigned
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), s.id, 0, 1, NULL, it.tech_id, 'Center', 'Teknisyen atandi.', s.t_assign
FROM seed s JOIN tmp_tech it ON it.rn = s.init_rn
WHERE s.status>=1;

-- Teknisyen degisikligi (uygulamadaki davranisla ayni: FromStatus NULL, durum degismez)
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), s.id, NULL, 1, it.tech_id, ft.tech_id, 'Center', 'Teknisyen degistirildi.',
       s.t_assign + (s.d2_h*0.4) * interval '1 hour'
FROM seed s
JOIN tmp_tech it ON it.rn = s.init_rn
JOIN tmp_tech ft ON ft.rn = s.f_rn
WHERE s.has_change AND s.status>=1;

-- Assigned -> InProgress
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), id, 1, 2, NULL, NULL, 'Technician', 'Ise baslandi.', t_prog
FROM seed WHERE status>=2;

-- InProgress -> Completed
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), id, 2, 3, NULL, NULL, 'Technician', 'Onarim tamamlandi.', t_comp
FROM seed WHERE status>=3;

-- Completed -> Approved
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), id, 3, 4, NULL, NULL, 'Center', 'Musteri onayi alindi.', t_appr
FROM seed WHERE status>=4;

-- Approved -> Closed
INSERT INTO ticket_status_histories
  ("Id","ServiceTicketId","FromStatus","ToStatus","PreviousTechnicianId","NewTechnicianId","ChangedByType","Note","ChangedAt")
SELECT gen_random_uuid(), id, 4, 5, NULL, NULL, 'Center', 'Kayit kapatildi.', t_close
FROM seed WHERE status=5;

-- ---------- 7) Yorumlar (~%45 olasilikla, kayit basi 0-4) ----------
INSERT INTO comments ("Id","ServiceTicketId","AuthorType","AuthorId","Content","CreatedAt")
SELECT gen_random_uuid(), s.id,
  (ARRAY['Customer','Technician','Center'])[1+floor(random()*3)::int],
  NULL,
  (ARRAY['Parca siparis edildi, gelince takilacak.','Musteri evde yoktu, yeniden randevu alindi.',
         'Ariza tespit edildi, fiyat bilgisi verildi.','Ne zaman gelinecek acaba?',
         'Islem tamamlandi, tesekkurler.','Ayni sorun tekrar etti, kontrol rica ederim.'])[1+floor(random()*6)::int],
  s.created_at + (random() * GREATEST(3600,
      EXTRACT(EPOCH FROM (LEAST(NOW(), s.t_close) - s.created_at))))::int * interval '1 second'
FROM seed s
CROSS JOIN generate_series(1,4) g
WHERE s.status >= 1 AND random() < 0.45;

-- ---------- 8) Ek metadata (~%35 olasilikla, kayit basi 0-2, onay oncesi) ----------
INSERT INTO attachments
  ("Id","ServiceTicketId","FileName","ContentType","FileSizeBytes","StoragePath","UploadedByType","CreatedAt")
SELECT gen_random_uuid(), s.id,
  'ariza-'||s.i||'-'||g||'.jpg', 'image/jpeg',
  (50000 + random()*3000000)::bigint,
  'https://storage.ornek.com/tickets/'||s.id||'/foto-'||g||'.jpg',
  (ARRAY['Customer','Technician'])[1+floor(random()*2)::int],
  s.created_at + (random()*86400)::int * interval '1 second'
FROM seed s
CROSS JOIN generate_series(1,2) g
WHERE random() < 0.35;

COMMIT;

-- ---------- Dogrulama ----------
SELECT 'technicians' AS tablo, COUNT(*) FROM technicians
UNION ALL SELECT 'customers', COUNT(*) FROM customers
UNION ALL SELECT 'service_tickets', COUNT(*) FROM service_tickets
UNION ALL SELECT 'histories', COUNT(*) FROM ticket_status_histories
UNION ALL SELECT 'comments', COUNT(*) FROM comments
UNION ALL SELECT 'attachments', COUNT(*) FROM attachments;

SELECT "Status", COUNT(*) AS adet,
       ROUND(100.0*COUNT(*)/SUM(COUNT(*)) OVER (),1) AS yuzde
FROM service_tickets GROUP BY "Status" ORDER BY "Status";
