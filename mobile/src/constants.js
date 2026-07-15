export const STATUS_LABELS = {
  New: 'Yeni',
  Assigned: 'Atandi',
  InProgress: 'Devam Ediyor',
  Completed: 'Tamamlandi',
  Approved: 'Onaylandi',
  Closed: 'Kapandi',
};

export const STATUS_COLORS = {
  New: '#2563eb',
  Assigned: '#7c3aed',
  InProgress: '#d97706',
  Completed: '#059669',
  Approved: '#0891b2',
  Closed: '#6b7280',
};

export const PRIORITY_LABELS = {
  Low: 'Dusuk',
  Medium: 'Orta',
  High: 'Yuksek',
  Critical: 'Kritik',
};

export const PRIORITY_COLORS = {
  Low: '#6b7280',
  Medium: '#2563eb',
  High: '#d97706',
  Critical: '#dc2626',
};

export const ALL_STATUSES = ['New', 'Assigned', 'InProgress', 'Completed', 'Approved', 'Closed'];

export const ALL_PRIORITIES = ['Critical', 'High', 'Medium', 'Low'];

// Backend'in kabul ettigi siralama alanlari (whitelist'li switch ile eslesir)
export const SORT_OPTIONS = [
  { key: 'createdAt-desc', label: 'En yeni', sortBy: 'createdAt', sortDir: 'desc' },
  { key: 'slaDeadline-asc', label: 'SLA yakin', sortBy: 'slaDeadline', sortDir: 'asc' },
  { key: 'priority-desc', label: 'Oncelik', sortBy: 'priority', sortDir: 'desc' },
];

export const SLA_OPTIONS = [
  { value: null, label: 'Tumu' },
  { value: true, label: 'SLA asildi' },
  { value: false, label: 'SLA icinde' },
];

// Gercek tarih araligi secici yerine hazir araliklar: operasyonda
// "3 Mart - 17 Nisan arasi" degil "son 24 saat" sorulur.
export const DATE_OPTIONS = [
  { key: 'all', label: 'Tumu', hours: null },
  { key: '24h', label: 'Son 24 saat', hours: 24 },
  { key: '7d', label: 'Son 7 gun', hours: 168 },
  { key: '30d', label: 'Son 30 gun', hours: 720 },
];
