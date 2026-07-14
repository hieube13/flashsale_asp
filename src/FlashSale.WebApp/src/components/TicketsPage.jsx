import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { Search, Filter, SlidersHorizontal, ArrowLeft, Calendar, MapPin, ArrowRight, Loader2 } from 'lucide-react'
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

export default function TicketsPage() {
  const [searchTerm, setSearchTerm] = useState('')
  const [selectedCategory, setSelectedCategory] = useState('All')
  const [tickets, setTickets] = useState([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const fetchTickets = async () => {
      try {
        const data = await ticketService.getActiveTickets()
        setTickets(data || [])
      } catch (error) { console.error('Failed to fetch tickets:', error) }
      finally { setLoading(false) }
    }
    fetchTickets()
  }, [])

  const filteredTickets = tickets.filter(t => {
    const matchesSearch = t.name && t.name.toLowerCase().includes(searchTerm.toLowerCase())
    const matchesCategory = selectedCategory === 'All' || t.category === selectedCategory
    return matchesSearch && matchesCategory
  })

  return (
    <div style={{ background: '#f8fafc', paddingBottom: '80px' }}>
      <div style={{ background: 'var(--color-primary)', padding: '40px 0 100px', color: 'white' }}>
        <div className="container">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '40px' }}>
            <div>
              <Link to="/" style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'rgba(255,255,255,0.7)', fontSize: '14px', marginBottom: '16px', textDecoration: 'none' }}><ArrowLeft size={16} /> Quay lại trang chủ</Link>
              <h1 style={{ fontSize: '40px', fontWeight: 900 }}>Khám phá sự kiện</h1>
            </div>
            <div style={{ background: 'rgba(255,255,255,0.1)', padding: '12px 20px', borderRadius: '12px' }}>
              <div style={{ fontSize: '12px', opacity: 0.7, marginBottom: '4px' }}>Tổng sự kiện</div>
              <div style={{ fontSize: '24px', fontWeight: 800 }}>{tickets.length}</div>
            </div>
          </div>
          <div style={{ position: 'relative', maxWidth: '700px' }}>
            <input type="text" placeholder="Tìm kiếm concert, sự kiện, nghệ sĩ..." value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} style={{ width: '100%', padding: '18px 24px 18px 56px', borderRadius: '16px', border: 'none', fontSize: '16px', boxShadow: '0 10px 25px -5px rgba(0,0,0,0.3)', outline: 'none' }} />
            <Search size={22} style={{ position: 'absolute', left: '20px', top: '50%', transform: 'translateY(-50%)', color: '#94a3b8' }} />
          </div>
        </div>
      </div>
      <div className="container" style={{ marginTop: '-40px' }}>
        <div style={{ display: 'grid', gridTemplateColumns: '300px 1fr', gap: '32px' }}>
          <aside>
            <div style={{ background: 'white', padding: '32px', borderRadius: '20px', boxShadow: '0 4px 20px -2px rgba(0,0,0,0.05)', border: '1px solid #f1f5f9', position: 'sticky', top: '100px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '28px', color: 'var(--color-primary)', fontWeight: 800, fontSize: '18px' }}><Filter size={20} />BỘ LỌC</div>
              <div style={{ marginBottom: '36px' }}>
                <label style={{ fontSize: '11px', fontWeight: 800, color: '#94a3b8', letterSpacing: '1.5px', marginBottom: '16px', display: 'block', textTransform: 'uppercase' }}>DANH MỤC</label>
                {['All','Music','Sports','Theater','Festival'].map(cat => (
                  <button key={cat} onClick={() => setSelectedCategory(cat)} style={{ display: 'flex', width: '100%', padding: '12px 16px', borderRadius: '12px', fontSize: '14px', fontWeight: 700, textAlign: 'left', background: selectedCategory === cat ? 'var(--color-accent-light)' : 'transparent', color: selectedCategory === cat ? 'var(--color-accent-hover)' : '#475569', border: 'none', cursor: 'pointer', marginBottom: '8px' }}>
                    {cat === 'All' ? 'Tất cả sự kiện' : cat}
                  </button>
                ))}
              </div>
              <button style={{ marginTop: '32px', padding: '14px', borderRadius: '12px', width: '100%', background: 'var(--color-accent)', color: 'var(--color-primary-dark)', fontWeight: 700, border: 'none', cursor: 'pointer' }}>ÁP DỤNG BỘ LỌC</button>
            </div>
          </aside>
          <main>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: 'white', padding: '16px 24px', borderRadius: '16px', boxShadow: '0 2px 10px rgba(0,0,0,0.02)', marginBottom: '24px', border: '1px solid #f1f5f9' }}>
              <div style={{ fontSize: '15px', color: '#64748b' }}>Hiển thị <strong>{filteredTickets.length}</strong> sự kiện phù hợp</div>
            </div>
            {loading ? <div style={{ display: 'flex', justifyContent: 'center', padding: '100px 0' }}><Loader2 className="animate-spin" size={40} color="var(--color-primary)" /></div>
              : filteredTickets.length > 0 ? (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: '24px' }}>
                  {filteredTickets.map(ticket => {
                    const stockAvailable = ticket.stockAvailable || 0
                    const stockTotal = ticket.stockInitial || 100
                    const priceToUse = ticket.priceFlash || ticket.priceOriginal || 0
                    return (
                      <div key={ticket.id} style={{ background: 'white', borderRadius: '16px', border: '1px solid #f1f5f9', overflow: 'hidden', boxShadow: '0 4px 15px rgba(0,0,0,0.05)' }}>
                        <div style={{ position: 'relative' }}>
                          <span style={{ position: 'absolute', top: '16px', left: '16px', zIndex: 10, background: ticket.priceFlash ? 'var(--color-accent)' : 'var(--color-primary)', color: ticket.priceFlash ? 'var(--color-primary-dark)' : 'white', padding: '4px 10px', borderRadius: '4px', fontSize: '11px', fontWeight: 700 }}>{ticket.priceFlash ? 'Hot Sale' : 'Mới'}</span>
                          <img src={ticket.image || 'https://images.unsplash.com/photo-1540039155733-5bb30b53aa14?w=400&h=300&fit=crop'} alt={ticket.name} style={{ width: '100%', height: '220px', objectFit: 'cover' }} />
                        </div>
                        <div style={{ padding: '24px' }}>
                          <h3 style={{ fontSize: '16px', fontWeight: 800, marginBottom: '16px', height: '48px', overflow: 'hidden', display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical' }}>{ticket.name}</h3>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginBottom: '20px' }}>
                            <span style={{ fontSize: '13px', display: 'flex', alignItems: 'center', gap: '5px', color: '#64748b' }}><Calendar size={14} />{formatDate(ticket.startTime)}</span>
                            <span style={{ fontSize: '13px', display: 'flex', alignItems: 'center', gap: '5px', color: '#64748b' }}><MapPin size={14} />{ticket.location || 'Địa điểm đang cập nhật'}</span>
                          </div>
                          <div style={{ marginBottom: '24px', padding: '12px', background: '#f8fafc', borderRadius: '12px' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '12px', marginBottom: '8px', fontWeight: 600 }}><span style={{ color: '#64748b' }}>Đã bán {stockTotal - stockAvailable} vé</span><span style={{ color: 'var(--color-primary)' }}>{Math.round(((stockTotal - stockAvailable) / stockTotal) * 100)}%</span></div>
                            <div style={{ height: '6px', background: '#e2e8f0', borderRadius: '3px', overflow: 'hidden' }}><div style={{ width: Math.round(((stockTotal - stockAvailable) / stockTotal) * 100) + '%', height: '100%', background: 'var(--color-primary)' }} /></div>
                          </div>
                          <div style={{ height: '60px', display: 'flex', flexDirection: 'column', justifyContent: 'center', marginBottom: '24px' }}>
                            {ticket.priceFlash ? (
                              <><span style={{ fontSize: '13px', color: '#94a3b8', textDecoration: 'line-through' }}>{formatPrice(ticket.priceOriginal)}</span>
                              <span style={{ fontSize: '24px', fontWeight: 900, color: 'var(--color-hot)' }}>{formatPrice(ticket.priceFlash)}</span></>
                            ) : <span style={{ fontSize: '24px', fontWeight: 900, color: 'var(--color-primary)' }}>{formatPrice(priceToUse)}</span>}
                          </div>
                          <Link to={"/ticket/" + ticket.id} style={{ display: 'flex', width: '100%', padding: '16px', borderRadius: '12px', fontSize: '14px', fontWeight: 800, alignItems: 'center', justifyContent: 'center', gap: '8px', background: 'var(--color-primary)', color: 'white', textDecoration: 'none' }}>MUA VÉ NGAY <ArrowRight size={16} /></Link>
                        </div>
                      </div>
                    )
                  })}
                </div>
              ) : (
                <div style={{ textAlign: 'center', padding: '100px 0', background: 'white', borderRadius: '20px', boxShadow: '0 4px 20px rgba(0,0,0,0.05)' }}>
                  <Search size={32} style={{ color: '#94a3b8', marginBottom: '16px' }} />
                  <h3 style={{ fontSize: '18px', fontWeight: 700, color: 'var(--color-primary)', marginBottom: '8px' }}>Không tìm thấy sự kiện</h3>
                  <p style={{ color: '#64748b', fontSize: '14px' }}>Vui lòng điều chỉnh bộ lọc hoặc từ khóa tìm kiếm nhé!</p>
                </div>
              )}
          </main>
        </div>
      </div>
    </div>
  )
}
