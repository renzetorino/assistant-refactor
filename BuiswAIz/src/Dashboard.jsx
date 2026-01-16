import { useNavigate } from "react-router-dom";
import React, { useState, useEffect } from "react";
import { formatDistanceToNow } from "date-fns";
import { supabase } from "./supabase"; 
import {
  LineChart, Line, XAxis, YAxis, Tooltip, CartesianGrid, ResponsiveContainer,
} from 'recharts';
import TopSellingProducts from "./Dashboard/TopSellingProducts";
import SalesSummaryDashboard from "./Dashboard/SalesSummaryDashboard";
import DailyGrossSales from "./Dashboard/DailyGrossSales";
import Notifications from "./Dashboard/Notifications";
import { fetchMorningBriefing, fetchExpenseForecast } from "./api/mentorInsights";
import InsightModal from "./components/InsightModal";
import "./stylecss/Dashboard/Dashboard.css";

const Dashboard = () => {
  const navigate = useNavigate(); 

  const [user, setUser] = useState(null);
  const [userBusinessId, setUserBusinessId] = useState(null);
  const [businessinfo, setBusinessInfo] = useState(null);
  const [loading, setLoading] = useState(true);
  const [topSellingProducts, setTopSellingProducts] = useState([]);
  const [leastSellingProducts, setLeastSellingProducts] = useState([]);
  const [notSellingProducts, setNotSellingProducts] = useState([]);
  const [productsLoading, setProductsLoading] = useState(true);
  const [productsError, setProductsError] = useState(null);
  const [expenseChartData, setExpenseChartData] = useState([]);
  const [activityLogs, setActivityLogs] = useState([]);

  
  // Help tooltip states for other components
  const [showHelpDailySales, setShowHelpDailySales] = useState(false);
  const [showHelpProducts, setShowHelpProducts] = useState(false);
  const [showHelpNotifications, setShowHelpNotifications] = useState(false);
  const [showHelpActivity, setShowHelpActivity] = useState(false);

  // Morning Briefing Insights state (Business Summary)
  const [showMorningBriefingModal, setShowMorningBriefingModal] = useState(false);
  const [morningBriefingContent, setMorningBriefingContent] = useState(null);
  const [morningBriefingLoading, setMorningBriefingLoading] = useState(false);
  const [morningBriefingError, setMorningBriefingError] = useState(null);

  // Expense Forecast Insights state
  const [showExpenseForecastModal, setShowExpenseForecastModal] = useState(false);
  const [expenseForecastContent, setExpenseForecastContent] = useState(null);
  const [expenseForecastLoading, setExpenseForecastLoading] = useState(false);
  const [expenseForecastError, setExpenseForecastError] = useState(null);

  useEffect(() => {
    const fetchUser = async () => {
      const { data: { user }, error } = await supabase.auth.getUser();
      if (error || !user) {
        navigate('/login');
        return;
      }

      const { data: profile, error: profileError } = await supabase
        .from('systemuser')
        .select('*')
        .eq('userid', user.id)
        .maybeSingle();

      if (profileError || !profile?.username || !profile?.business_id) {
        navigate('/setup-business');
        return;
      }

      setUser(profile);
    };

    fetchUser();
  }, [navigate]);


  function downloadTemplate() {
    const headers = [
      "orderid",
      "orderdate",     
      "productname",
      "color",
      "agesize",
      "quantity",
      "unitprice",
      "subtotal",     
      "amountpaid",
    ];

    // one helpful example row
    const sample = [
      "10001",
      "2025-10-04",
      "Basic Tee",
      "Black",
      "M",
      "2",
      "250",
      "500",
      "500",
    ];

    const hasXLSX = typeof window !== "undefined" && window.XLSX;

    if (hasXLSX) {
      const ws = window.XLSX.utils.aoa_to_sheet([headers, sample]);
      const wb = window.XLSX.utils.book_new();
      window.XLSX.utils.book_append_sheet(wb, ws, "Sales Upload Template");
      const wbout = window.XLSX.write(wb, { bookType: "xlsx", type: "array" });
      const blob = new Blob([wbout], {
        type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "sales_upload_template.xlsx";
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } else {
      // CSV fallback
      const rows = [headers, sample];
      const csv = rows
        .map(r =>
          r
            .map(v => {
              const s = String(v ?? "");
              return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
            })
            .join(",")
        )
        .join("\n");
      const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "sales_upload_template.csv";
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    }
  }


  function getExpenseDate(row) {
    const raw =
      row.occured_on ??
      row.occurred_on ??
      row.expensedate ??
      row.expense_date ??
      row.expenseDate ??
      row.date ??
      row.created_at;

    if (!raw) return null;

    if (typeof raw === "string" && /^\d{4}-\d{2}-\d{2}$/.test(raw)) {
      const [y, m, d] = raw.split("-").map(Number);
      return new Date(y, m - 1, d);
    }

    const d = new Date(raw);
    return isNaN(d) ? null : d;
  }

  function buildDailySeries(rows, opts = {}) {
    const now = new Date();
    const year = opts.year ?? now.getFullYear();
    const monthIndex = opts.monthIndex ?? now.getMonth();

    const lastDay = new Date(year, monthIndex + 1, 0);
    const daysInMonth = lastDay.getDate();

    const byDay = Array.from({ length: daysInMonth }, () => 0);

    for (const row of rows) {
      const amt = Number(row.amount ?? 0);

      const d = getExpenseDate(row);
      if (!d) continue;

      if (d.getFullYear() === year && d.getMonth() === monthIndex) {
        const dayIdx = d.getDate() - 1;
        byDay[dayIdx] += Number.isFinite(amt) ? amt : 0;
      }
    }

    return byDay.map((total, i) => ({
      day: i + 1,
      total: Number(total.toFixed(2)),
    }));
  }

  // Fetch user authentication and business information
  useEffect(() => {
    let mounted = true;

    const getUser = async () => {
      try {
        const { data: { user }, error } = await supabase.auth.getUser();
        if (!mounted) return;

        if (error || !user) {
          window.location.href = '/';
          return;
        }
        
        const { data: profile, error: profileError } = await supabase
          .from('systemuser')
          .select('*, business_id')
          .eq('userid', user.id)
          .single();
        
        if (!mounted) return;
        
        if (profileError) {
          console.error("Error fetching user profile:", profileError);
          return;
        }

        if (!profile.business_id) {
          alert('No business assigned to your account. Please contact administrator.');
          window.location.href = '/Dashboard';
          return;
        }

        setUser(profile);
        setUserBusinessId(profile.business_id);

        // Fetch business details
        const { data: business, error: businessError } = await supabase
          .from('business_role')
          .select('*')
          .eq('businessid', profile.business_id)
          .single();

        if (!mounted) return;

        if (businessError) {
          console.error("Error fetching business info:", businessError);
          return;
        }

        setBusinessInfo(business);
        setLoading(false);
        
        // Sprint 6: Trigger background refresh of AI insights on login
        // This pre-populates the cache so ? icon clicks are instant
        try {
          const { data: { session } } = await supabase.auth.getSession();
          if (session?.access_token) {
            const API_BASE = import.meta.env.VITE_API_ASSISTANT_URL || 'http://localhost:5115';
            
            // Fire-and-forget: don't wait for response, don't block UI
            fetch(`${API_BASE}/api/mentor-insights/refresh-async`, {
              method: 'POST',
              headers: {
                'Authorization': `Bearer ${session.access_token}`,
                'Content-Type': 'application/json',
              },
            }).then(response => {
              if (response.ok) {
                console.log('[Dashboard] AI Insights refresh queued successfully');
              } else {
                console.warn('[Dashboard] AI Insights refresh failed:', response.status);
              }
            }).catch(error => {
              console.warn('[Dashboard] AI Insights refresh error:', error.message);
            });
          }
        } catch (refreshError) {
          console.warn('[Dashboard] Failed to trigger insights refresh:', refreshError);
          // Don't block user experience if refresh fails
        }
        
      } catch (error) {
        console.error("Authentication error:", error);
        if (mounted) window.location.href = '/';
      }
    };
    
    getUser();
    return () => { mounted = false; };
  }, []);

  // Load expense chart data - ONLY when userBusinessId is available
  useEffect(() => {
    const loadChartData = async () => {
      // ✅ CRITICAL: Don't fetch if no business ID
      if (!userBusinessId) {
        console.log('Waiting for userBusinessId...');
        return;
      }

      const now = new Date();
      const y = now.getFullYear();
      const m = now.getMonth();

      const startStr = `${y}-${String(m + 1).padStart(2, "0")}-01`;
      const nextMonth = m === 11 ? 0 : m + 1;
      const nextYear  = m === 11 ? y + 1 : y;
      const nextStr = `${nextYear}-${String(nextMonth + 1).padStart(2, "0")}-01`;

      const { data, error } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount, business_id")
        .eq("business_id", userBusinessId) // Filter by business
        .gte("occurred_on", startStr)
        .lt("occurred_on", nextStr);

      if (error) {
        console.error("Failed to fetch expenses for chart:", error);
        setExpenseChartData([]);
        return;
      }

      const daily = buildDailySeries(data, { year: y, monthIndex: m });
      setExpenseChartData(daily);
    };

    loadChartData();
  }, [userBusinessId]);

  // Load activity logs - ONLY when userBusinessId is available
  const loadActivityLogs = async () => {
    // ✅ CRITICAL: Don't fetch if no business ID
    if (!userBusinessId) {
      console.log('Waiting for userBusinessId for activity logs...');
      return;
    }

    const { data, error } = await supabase
      .from("activitylog")
      .select("*, systemuser(username)")
      .eq("businessid", userBusinessId) // Filter by business
      .order("created_at", { ascending: false });

    if (error) {
      console.error("Failed to fetch activity logs:", error);
      return;
    }

    setActivityLogs(data.slice(0, 50)); 

    if (data.length > 50) {
      const logsToDelete = data.slice(50); 
      const idsToDelete = logsToDelete.map(log => log.activity_id); 

      const { error: deleteError } = await supabase
        .from("activitylog")
        .delete()
        .in("activity_id", idsToDelete);

      if (deleteError) {
        console.error("Failed to delete old logs:", deleteError);
      } else {
        console.log(`Deleted ${idsToDelete.length} old logs.`);
      }
    }
  };

  useEffect(() => {
    if (userBusinessId) {
      loadActivityLogs();
      const id = setInterval(loadActivityLogs, 5000);
      return () => clearInterval(id);
    }
  }, [userBusinessId]);

  // Fetch top selling products - ONLY when userBusinessId is available
  const fetchTopSellingProducts = async () => {
    // ✅ CRITICAL: Don't fetch if no business ID
    if (!userBusinessId) {
      console.log('Waiting for userBusinessId for products...');
      setProductsLoading(false);
      return;
    }

    try {
      setProductsLoading(true);

      // First get orders for this business
      const { data: businessOrders, error: ordersError } = await supabase
        .from('orders')
        .select('orderid')
        .eq('businessid', userBusinessId);

      if (ordersError) throw ordersError;

      if (!businessOrders || businessOrders.length === 0) {
        setTopSellingProducts([]);
        setLeastSellingProducts([]);
        
        // Get all products for this business for "not selling"
        const { data: allProducts } = await supabase
          .from('products')
          .select('productid, productname, image_url')
          .eq('businessid', userBusinessId);
        
        const notSelling = (allProducts || []).slice(0, 10).map(product => ({
          productid: product.productid,
          productname: product.productname,
          image_url: product.image_url,
          totalQuantity: 0,
          timesBought: 0,
        }));
        setNotSellingProducts(notSelling);
        setProductsLoading(false);
        return;
      }

      const orderIds = businessOrders.map(o => o.orderid);

      // Get order items for these orders
      const { data: orderData, error: orderError } = await supabase
        .from('orderitems')
        .select(`
          orderid,
          productid,
          quantity,
          unitprice,
          subtotal,
          createdat
        `)
        .in('orderid', orderIds);

      if (orderError) throw orderError;

      // Get all products for this business
      const { data: allProducts, error: productsError } = await supabase
        .from('products')
        .select('productid, productname, image_url, businessid')
        .eq('businessid', userBusinessId);

      if (productsError) throw productsError;

      const summary = {};
      orderData.forEach(item => {
        const id = item.productid;
        const product = allProducts.find(p => p.productid === id);
        const name = product?.productname || 'Unknown';
        const imageUrl = product?.image_url || '';

        if (!summary[id]) {
          summary[id] = {
            productid: id,
            productname: name,
            image_url: imageUrl,
            totalQuantity: 0,
            timesBought: new Set(),
          };
        }

        summary[id].totalQuantity += item.quantity;
        summary[id].timesBought.add(item.orderid);
      });

      const sellingArray = Object.values(summary).map(item => ({
        ...item,
        timesBought: item.timesBought.size,
      }));

      sellingArray.sort((a, b) => b.totalQuantity - a.totalQuantity);

      const topSelling = sellingArray.slice(0, 5);
      setTopSellingProducts(topSelling);

      const leastSelling = sellingArray.length > 5 
        ? sellingArray.slice(-5).reverse() 
        : [];
      setLeastSellingProducts(leastSelling);

      const soldProductIds = new Set(sellingArray.map(p => p.productid));
      const notSelling = allProducts
        .filter(product => !soldProductIds.has(product.productid))
        .slice(0, 10)
        .map(product => ({
          productid: product.productid,
          productname: product.productname,
          image_url: product.image_url,
          totalQuantity: 0,
          timesBought: 0,
        }));
      setNotSellingProducts(notSelling);

      setProductsError(null);
    } catch (error) {
      console.error('Error fetching top selling products:', error);
      setProductsError('Failed to load top selling products');
      setTopSellingProducts([]);
      setLeastSellingProducts([]);
      setNotSellingProducts([]);
    } finally {
      setProductsLoading(false);
    }
  };

  useEffect(() => {
    if (userBusinessId) {
      fetchTopSellingProducts();
    }
  }, [userBusinessId]);

  // Fetch mentor insights when help button is clicked
  const handleMorningBriefingClick = async () => {
    setShowMorningBriefingModal(true);
    setMorningBriefingLoading(true);
    setMorningBriefingError(null);

    try {
      const data = await fetchMorningBriefing();
      setMorningBriefingContent(data.content || "No briefing available.");
    } catch (error) {
      console.error('Failed to fetch morning briefing:', error);
      setMorningBriefingError(error.message || 'Failed to load insights. Please try again.');
    } finally {
      setMorningBriefingLoading(false);
    }
  };

  // Expense Forecast modal handler
  const handleExpenseForecastClick = async () => {
    setShowExpenseForecastModal(true);
    setExpenseForecastLoading(true);
    setExpenseForecastError(null);

    try {
      const data = await fetchExpenseForecast();
      setExpenseForecastContent(data.content || "No expense forecast available.");
    } catch (error) {
      console.error('Failed to fetch expense forecast:', error);
      setExpenseForecastError(error.message || 'Failed to load insights. Please try again.');
    } finally {
      setExpenseForecastLoading(false);
    }
  };

  // ✅ Show loading state while waiting for business ID
  if (loading || !userBusinessId) {
    return (
      <div className="dashboard-page">
        <header className="header-bar">
          <h1 className="header-title">BuiswAIz</h1>
        </header>
        <div className="main-section" style={{ 
          display: 'flex', 
          alignItems: 'center', 
          justifyContent: 'center',
          minHeight: '80vh'
        }}>
          <div style={{ textAlign: 'center' }}>
            <p style={{ fontSize: '18px', color: '#666' }}>Loading dashboard...</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="dashboard-page">
      <header className="header-bar">
        <h1 className="header-title">BuiswAIz</h1>
      </header>

      <div className="main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li className="active">Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              <li onClick={() => navigate("/expenses")}>Expenses</li>
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
              {/* COMMENTED OUT: Business Maturity link removed (feature did not meet team/advisor standards) */}
              {/* <li onClick={() => navigate("/maturity-report")}>Business Maturity</li> */}
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              <li onClick={() => navigate("/pos")}>Point of Sales</li>
              <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li>
            </ul>
          </div>
        </aside>

        <div className="main-content">
          <div className="dashboard-content">
            {/* Sales Summary with Help */}
            <div className="dashboard-panel sales-summary">
              <div className="panel-header-with-help">
                <div className="header-left-dash">
                  <h3>Business Summary</h3>
                  <div className="help-wrapper-dash">
                    <button 
                      className="help-button-dash"
                      onClick={handleMorningBriefingClick}
                      aria-label="AI Morning Briefing"
                    >
                      ?
                    </button>
                  </div>
                </div>
              </div>
              <div className="panel-content-summary">
                <SalesSummaryDashboard userBusinessId={userBusinessId} />
              </div>
            </div>

            <div className="charts-section">
              {/* Daily Sales with Help */}
              <div className="dashboard-panel daily-sales">
                <div className="panel-header-with-help">
                  <div className="header-left-dash">
                    <h3>Daily Gross Sales</h3>
                    <div className="help-wrapper-dash">
                      <button 
                        className="help-button-dash"
                        onClick={() => setShowHelpDailySales(!showHelpDailySales)}
                        aria-label="Help"
                      >
                        ?
                      </button>
                      {showHelpDailySales && (
                        <div className="help-box-dash">
                          <div className="help-arrow-dash"></div>
                          
                          <div className="help-content-dash">
                            <p>Gen TIPS</p>
                          </div>
                          
                          <div className="help-separator-dash"></div>
                          
                          <div className="help-content-dash">
                            <p>AI</p>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
                <div className="panel-content">
                  <DailyGrossSales userBusinessId={userBusinessId} />
                </div>
              </div>

              <div className="bottom-section">
                {/* Monthly Expense with Help */}
                <div className="dashboard-panel monthly-expense">
                  <div className="panel-header-with-help">
                    <div className="header-left-dash">
                      <h3>Monthly Expense</h3>
                      <div className="help-wrapper-dash">
                        <button 
                          className="help-button-dash"
                          onClick={handleExpenseForecastClick}
                          aria-label="AI Expense Insights"
                        >
                          ?
                        </button>
                      </div>
                    </div>
                  </div>
                  <div className="panel-content" style={{ minWidth: 0 }}>
                    {expenseChartData.length === 0 ? (
                      <p style={{ padding: 12 }}>No expense data yet.</p>
                    ) : (
                      <ResponsiveContainer width="90%" height="105%">
                        <LineChart data={expenseChartData}>
                          <XAxis dataKey="day" />
                          <YAxis domain={[0, (dataMax) => (dataMax && dataMax > 0 ? dataMax : 1)]} />
                          <Tooltip />
                          <CartesianGrid strokeDasharray="5 5" />
                          <Line
                            type="monotone"
                            dataKey="total"
                            stroke="#3b82f6"
                            strokeWidth={2}
                            dot={false}
                          />
                        </LineChart>
                      </ResponsiveContainer>
                    )}
                  </div>
                </div>

                {/* Top Selling with Help */}
                <div className="dashboard-panel top-selling">
                  <div className="panel-header-with-help">
                    <div className="header-left-dash">
                      <h3>Products Performance</h3>
                      <div className="help-wrapper-dash">
                        <button 
                          className="help-button-dash"
                          onClick={() => setShowHelpProducts(!showHelpProducts)}
                          aria-label="Help"
                        >
                          ?
                        </button>
                        {showHelpProducts && (
                          <div className="help-box-dash">
                            <div className="help-arrow-dash"></div>
                            
                            <div className="help-content-dash">
                              <p>Gen TIPS</p>
                            </div>
                            
                            <div className="help-separator-dash"></div>
                            
                            <div className="help-content-dash">
                              <p>AI</p>
                            </div>
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                  <div className="panel-content">
                    {productsLoading ? (
                      <div className="loading-state">
                        <p>Loading products...</p>
                      </div>
                    ) : productsError ? (
                      <div className="error-state">
                        <p>{productsError}</p>
                      </div>
                    ) : (
                      <TopSellingProducts 
                        topSellingProducts={topSellingProducts}
                        leastSellingProducts={leastSellingProducts}
                        notSellingProducts={notSellingProducts}
                        userBusinessId={userBusinessId}
                      />
                    )}
                  </div>
                </div> 
              </div>
            </div>
          </div>

          <div className="right-panel">
            <div className="user-info-card">
              <div className="user-left">
                <div className="user-avatar" />
                <div className="user-username">
                  {user?.username || "No username found"}
                </div>
              </div>
              <button
                className="logout-button"
                onClick={async () => {
                  await supabase.auth.signOut();
                  localStorage.clear();
                  window.location.href = "/";
                }}
              >
                ⏻
              </button>
            </div>

            {/* Notifications with Help */}
            <div className="notification-panel">
              <div className="panel-header-with-help">
                <div className="header-left-dash">
                  <h3>Notifications</h3>
                  <div className="help-wrapper-dash">
                    <button 
                      className="help-button-dash"
                      onClick={() => setShowHelpNotifications(!showHelpNotifications)}
                      aria-label="Help"
                    >
                      ?
                    </button>
                    {showHelpNotifications && (
                      <div className="help-box-dash">
                        <div className="help-arrow-dash"></div>
                        
                        <div className="help-content-dash">
                          <p>Gen TIPS</p>
                        </div>
                        
                        <div className="help-separator-dash"></div>
                        
                        <div className="help-content-dash">
                          <p>AI</p>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>
              <div className="activity-container">
                <Notifications userBusinessId={userBusinessId} />
              </div>
            </div>

            {/* Activity with Help */}
            <div className="activity-panel">
              <div className="panel-header-with-help">
                <div className="header-left-dash">
                  <h3>Recent Activity</h3>
                  <div className="help-wrapper-dash">
                    <button 
                      className="help-button-dash"
                      onClick={() => setShowHelpActivity(!showHelpActivity)}
                      aria-label="Help"
                    >
                      ?
                    </button>
                    {showHelpActivity && (
                      <div className="help-box-dash">
                        <div className="help-arrow-dash"></div>
                        
                        <div className="help-content-dash">
                          <p>Gen TIPS</p>
                        </div>
                        
                        <div className="help-separator-dash"></div>
                        
                        <div className="help-content-dash">
                          <p>AI</p>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>
              <div className="activity-container">
                <ul className="activity-list">
                  {activityLogs.length === 0 ? (
                    <li className="activity-item no-activity">No recent activity</li>
                  ) : (
                    activityLogs.map((log, i) => (
                      <li key={i} className="activity-item">
                        <div className="activity-content">
                          <span className="activity-description">
                            <span className="log-username">
                              {log.systemuser?.username || "Someone"}
                            </span>{" "}
                            {log.action_desc}
                          </span>
                          <span className="activity-time">
                            {formatDistanceToNow(new Date(log.created_at), { addSuffix: true })}
                          </span>
                        </div>
                      </li>
                    ))
                  )}
                </ul>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Morning Briefing Modal */}
      <InsightModal
        isOpen={showMorningBriefingModal}
        onClose={() => setShowMorningBriefingModal(false)}
        title="👋 Your Business Overview"
        content={morningBriefingContent}
        loading={morningBriefingLoading}
        error={morningBriefingError}
        insightType="morning-briefing"
      />

      {/* Expense Forecast Modal */}
      <InsightModal
        isOpen={showExpenseForecastModal}
        onClose={() => setShowExpenseForecastModal(false)}
        title="� Expense Insights - Smart Spending Guide"
        content={expenseForecastContent}
        loading={expenseForecastLoading}
        error={expenseForecastError}
        insightType="expense-forecast"
      />
    </div>
  );
};

export default Dashboard;