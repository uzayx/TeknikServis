import { API_URL } from './config';

// Backend hatalari RFC 7807 ProblemDetails formatinda doner
// (title, detail, errorCode...). Bu sarmalayici, hatayi tek yerde
// yakalayip ekranlara kullanici-dostu mesaj olarak iletir.
async function request(path, options = {}) {
  let response;
  try {
    response = await fetch(`${API_URL}${path}`, {
      headers: { 'Content-Type': 'application/json' },
      ...options,
    });
  } catch (e) {
    throw new Error('Sunucuya ulasilamiyor. Ag baglantisini ve API adresini kontrol edin.');
  }

  if (response.ok) {
    if (response.status === 204) return null;
    return response.json();
  }

  let problem = null;
  try { problem = await response.json(); } catch (_) { /* govde yok */ }

  // Validation hatasi (400): alan bazli mesajlari birlestir
  if (problem && problem.errors) {
    const messages = Object.values(problem.errors).flat().join('\n');
    throw new Error(messages || 'Dogrulama hatasi.');
  }
  // Is kurali / not found: backend'in detail mesaji zaten aciklayici
  if (problem && problem.detail) {
    throw new Error(problem.detail);
  }
  throw new Error(`Beklenmeyen hata (HTTP ${response.status}).`);
}

export const api = {
  getTickets: (params) => {
    const qs = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => {
      if (v !== null && v !== undefined && v !== '') qs.append(k, v);
    });
    return request(`/api/tickets?${qs.toString()}`);
  },
  getTicket: (id) => request(`/api/tickets/${id}`),
  createTicket: (body) => request('/api/tickets', { method: 'POST', body: JSON.stringify(body) }),
  changeStatus: (id, body) => request(`/api/tickets/${id}/status`, { method: 'POST', body: JSON.stringify(body) }),
  assignTechnician: (id, body) => request(`/api/tickets/${id}/assign`, { method: 'POST', body: JSON.stringify(body) }),
  getComments: (id) => request(`/api/tickets/${id}/comments`),
  addComment: (id, body) => request(`/api/tickets/${id}/comments`, { method: 'POST', body: JSON.stringify(body) }),
  getCustomers: () => request('/api/customers'),
  createCustomer: (body) => request('/api/customers', { method: 'POST', body: JSON.stringify(body) }),
  getTechnicians: () => request('/api/technicians'),
};
