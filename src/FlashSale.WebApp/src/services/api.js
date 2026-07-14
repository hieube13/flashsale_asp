import axios from 'axios';

const API_BASE_URL = '/ticket';

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const ticketService = {
  getActiveTickets: async () => {
    try {
      const response = await api.get('/active');
      return response.data.result;
    } catch (error) {
      console.error('Error fetching active tickets:', error);
      throw error;
    }
  },
  getTicketById: async (id) => {
    try {
      const response = await api.get('/' + id);
      return response.data.result;
    } catch (error) {
      console.error('Error fetching ticket ' + id + ':', error);
      throw error;
    }
  },
  createBooking: async ({ ticketId, quantity }) => {
    try {
      const response = await axios.post('/order/cas', { ticketId, quantity }, {
        headers: { 'Content-Type': 'application/json' },
      });
      return response.data.result;
    } catch (error) {
      console.error('Error creating booking:', error);
      throw error;
    }
  },
};

const orderApi = axios.create({
  baseURL: '/order',
  headers: { 'Content-Type': 'application/json' },
});

export const managerService = {
  createEvent: async (payload) => {
    const response = await api.post('/create', payload);
    return response.data.result;
  },
  getAllTickets: async () => {
    const response = await api.get('/active');
    return response.data.result;
  },
  activateTicket: async (id) => {
    const response = await api.put('/' + id + '/active');
    return response.data.result;
  },
  deactivateTicket: async (id) => {
    const response = await api.put('/' + id + '/inactive');
    return response.data.result;
  },
  deleteTicket: async (id) => {
    const response = await api.delete('/' + id);
    return response.data.result;
  },
  getOrdersAll: async (yearMonth) => {
    const response = await orderApi.get('/1/list?ntable=' + yearMonth);
    return response.data.result;
  },
  getOrders: async (yearMonth, cursor, limit) => {
    cursor = cursor || 0;
    limit = limit || 50;
    const response = await orderApi.get('/1/list/page?ntable=' + yearMonth + '&cursor=' + cursor + '&limit=' + limit);
    return response.data.result;
  },
  cancelOrder: async ({ userId, orderNumber }) => {
    const response = await orderApi.put('/' + userId + '/' + orderNumber + '/cancel');
    return response.data.result;
  },
};

export default api;
