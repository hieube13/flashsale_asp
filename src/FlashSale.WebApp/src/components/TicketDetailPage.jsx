import { useState, useEffect } from 'react'
import { useParams, Link, useNavigate } from 'react-router-dom'
import { Calendar, MapPin, ArrowLeft, Clock, Share2, Heart, ShieldCheck, Ticket, Info, Map as MapIcon, CheckCircle2, ChevronRight, Loader2 } from 'lucide-react'
import { ticketService } from '../services/api'

function formatPrice(price) {
  if (!price) return 'Liên hệ'
  return new Intl.NumberFormat('vi-VN').format(price) + 'đ'
}

function formatDate(dateStr) {
  if (!dateStr) return 'Đang cập nhật'
  try {
    const d = new Date(dateStr)
    return `${d.getDate().toString().padStart(2,'0')}-${(d.getMonth()+1).toString().padStart(2,'0')}-${d.getFullYear()}`
  } catch (e) { return dateStr }
}

export default function TicketDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [ticket, setTicket] = useState(null)
  const [loading, setLoading] = useState(true)
  const [selectedType, setSelectedType] = useState('Standard')
  const [quantity, setQuantity] = useState(1)
  useEffect(() => {
    const fetchTicket = async () => {
      try {
        const data = await ticketService.getTicketById(id)
        setTicket(data)
      } catch (error) { console.error('Failed to fetch ticket detail:', error) }
      finally { setLoading(false) }
    }
    fetchTicket()
    window.scrollTo(0, 0)
  }, [id])
  if (loading) return <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh', background: '#f8fafc' }}><Loader2 className="animate-spin" size={48} color="var(--color-primary)" /></div>
  if (!ticket) return <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', height: '100vh', gap: '20px' }}><h2>Không tìm thấy sự kiện</h2><Link to="/tickets" style={{ color: 'var(--color-primary)', fontWeight: 700 }}>Quay lại danh sách</Link></div>
  const priceToUse = ticket.priceFlash || ticket.priceOriginal || 0
  return (
    <div style={{ background: '#f8fafc', minHeight: '100vh' }}>
      <div style={{ background: 'white', borderBottom: '1px solid #f1f5f9', padding: '8px 0' }}>
        <div className="container" style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '11px', color: '#94a3b8' }}>
          <Link to="/" style={{ color: '#94a3b8', textDecoration: 'none' }}>Trang chủ</Link><ChevronRight size={10} /><Link to="/tickets" style={{ color: '#94a3b8', textDecoration: 'none' }}>Sự kiện</Link><ChevronRight size={10} /><span style={{ color: 'var(--color-primary)', fontWeight: 600 }}>{ticket.name}</span>
        </div>
      </div>
      <div className="container" style={{ paddingTop: '40px', paddingBottom: '60px' }}>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 340px', gap: '16px', alignItems: 'start' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
            <section style={{ background: 'white', padding: '24px', borderRadius: '16px', border: '1px solid #f1f5f9' }}>
              <h2 style={{ fontSize: '18px', fontWeight: 800, marginBottom: '16px' }}>{ticket.name}</h2>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '12px', marginBottom: '16px', color: '#475569' }}>
                <span style={{ display: 'flex', alignItems: 'center', gap: '5px', fontSize: '12px' }}><Calendar size={13} />{formatDate(ticket.startTime)}</span>
                <span style={{ display: 'flex', alignItems: 'center', gap: '5px', fontSize: '12px' }}><Clock size={13} />{ticket.startTime ? new Date(ticket.startTime).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'}) : '19:00'}</span>
                <span style={{ display: 'flex', alignItems: 'center', gap: '5px', fontSize: '12px' }}><MapPin size={13} />{ticket.location || 'Địa điểm đang cập nhật'}</span>
              </div>
              <p style={{ color: '#475569', lineHeight: '1.6', fontSize: '14px' }}>{ticket.description}</p>
            </section>
            <section style={{ background: 'white', padding: '24px', borderRadius: '16px', border: '1px solid #f1f5f9' }}>
              <h2 style={{ fontSize: '18px', fontWeight: 800, marginBottom: '16px' }}>Lợi ích</h2>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: '10px' }}>
                {[{title:'Chính hãng',icon:<CheckCircle2 size={16} />},{title:'Bảo mật',icon:<ShieldCheck size={16} />},{title:'Hỗ trợ 24/7',icon:<Info size={16} />},{title:'Nhận vé ngay',icon:<Ticket size={16} />}].map((item,i) => (
                  <div key={i} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px', padding: '12px 8px', background: '#f8fafc', borderRadius: '10px', textAlign: 'center' }}>
                    <div>{item.icon}</div><h4 style={{ fontWeight: 700, fontSize: '11px', color: '#475569' }}>{item.title}</h4>
                  </div>
                ))}
              </div>
            </section>
          </div>
          <aside style={{ position: 'sticky', top: '80px' }}>
            <div style={{ background: 'white', borderRadius: '16px', boxShadow: '0 15px 35px rgba(0,0,0,0.08)', overflow: 'hidden', border: '1px solid #f1f5f9' }}>
              <div style={{ background: 'var(--color-primary)', padding: '20px', color: 'white' }}>
                <div style={{ fontSize: '11px', opacity: 0.8, marginBottom: '2px' }}>Giá vé từ</div>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: '6px' }}>
                  <span style={{ fontSize: '24px', fontWeight: 900 }}>{formatPrice(priceToUse)}</span>
                  {ticket.priceFlash && <span style={{ fontSize: '13px', opacity: 0.6, textDecoration: 'line-through' }}>{formatPrice(ticket.priceOriginal)}</span>}
                </div>
              </div>
              <div style={{ padding: '20px' }}>
                <div style={{ marginBottom: '16px' }}>
                  <label style={{ fontSize: '10px', fontWeight: 800, color: '#94a3b8', letterSpacing: '0.5px', marginBottom: '10px', display: 'block' }}>SỐ LƯỢNG</label>
                  <div style={{ display: 'flex', alignItems: 'center', background: '#f8fafc', padding: '8px 14px', borderRadius: '8px', justifyContent: 'space-between', border: '1px solid #f1f5f9' }}>
                    <button onClick={() => setQuantity(Math.max(1, quantity - 1))} style={{ width: '28px', height: '28px', borderRadius: '6px', border: '1px solid #e2e8f0', background: 'white', fontWeight: 900, cursor: 'pointer' }}>-</button>
                    <span style={{ fontSize: '16px', fontWeight: 800 }}>{quantity}</span>
                    <button onClick={() => setQuantity(quantity + 1)} style={{ width: '28px', height: '28px', borderRadius: '6px', border: '1px solid #e2e8f0', background: 'white', fontWeight: 900, cursor: 'pointer' }}>+</button>
                  </div>
                </div>
                <div style={{ borderTop: '2px dashed #f8fafc', paddingTop: '16px', marginBottom: '16px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '6px', fontSize: '12px', color: '#64748b' }}><span>Tạm tính</span><span>{formatPrice(priceToUse * quantity)}</span></div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '16px', fontWeight: 900 }}><span>Tổng cộng</span><span style={{ color: 'var(--color-primary)' }}>{formatPrice(priceToUse * quantity)}</span></div>
                </div>
                <button style={{ width: '100%', padding: '14px', borderRadius: '10px', background: 'var(--color-primary)', color: 'white', fontSize: '15px', fontWeight: 800, border: 'none', cursor: 'pointer', marginBottom: '12px' }} onClick={() => navigate('/cart', { state: { ticket, quantity } })}>ĐẶT VÉ NGAY</button>
              </div>
            </div>
          </aside>
        </div>
      </div>
    </div>
  )
}
