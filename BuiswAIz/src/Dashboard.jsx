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
import "./stylecss/Dashboard/Dashboard.css";

const Dashboard = () => {
  const navigate = useNavigate(); 

  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const [topSellingProducts, setTopSellingProducts] = useState([]);
  const [leastSellingProducts, setLeastSellingProducts] = useState([]);
  const [notSellingProducts, setNotSellingProducts] = useState([]);
  const [productsLoading, setProductsLoading] = useState(true);
  const [productsError, setProductsError] = useState(null);
  const [expenseChartData, setExpenseChartData] = useState([]);
  const [activityLogs, setActivityLogs] = useState([]);


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
      const amt =
        Number(row.amount) ??
        Number(row.expense_amount) ??
        Number(row.total) ?? 0;

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

  useEffect(() => {
    const loadChartData = async () => {
      const now = new Date();
      const y = now.getFullYear();
      const m = now.getMonth();

      const startStr = `${y}-${String(m + 1).padStart(2, "0")}-01`;
      const nextMonth = m === 11 ? 0 : m + 1;
      const nextYear  = m === 11 ? y + 1 : y;
      const nextStr = `${nextYear}-${String(nextMonth + 1).padStart(2, "0")}-01`;

      const { data, error } = await supabase
        .from("expenses")
        .select("id, occurred_on, amount")
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
  }, []);

  const loadActivityLogs = async () => {
    const { data, error } = await supabase
      .from("activitylog")
      .select("*, systemuser(username)")
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
    loadActivityLogs();
    const id = setInterval(loadActivityLogs, 5000);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    const getUser = async () => {
      const { data: { user }, error } = await supabase.auth.getUser();
      if (error || !user) {
        window.location.href = '/';
        return;
      }

      const { data: profile, error: profileError } = await supabase
        .from('systemuser')
        .select('*')
        .eq('userid', user.id)
        .single();

      if (profileError) {
        console.error("Error fetching user profile:", profileError);
        setLoading(false);
        return;
      }

      setUser(profile);
      setLoading(false);
    };
    
    getUser();
  }, []);

  const fetchTopSellingProducts = async () => {
    try {
      setProductsLoading(true);

      // Fetch all order items
      const { data: orderData, error: orderError } = await supabase
        .from('orderitems')
        .select(`
          orderid,
          productid,
          quantity,
          unitprice,
          subtotal,
          createdat,
          products (productname, image_url)
        `);

      if (orderError) throw orderError;

      // Fetch all products
      const { data: allProducts, error: productsError } = await supabase
        .from('products')
        .select('productid, productname, image_url');

      if (productsError) throw productsError;

      // Process order items into summary
      const summary = {};
      orderData.forEach(item => {
        const id = item.productid;
        const name = item.products?.productname || 'Unknown';
        const imageUrl = item.products?.image_url || '';

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

      // Convert to array
      const sellingArray = Object.values(summary).map(item => ({
        ...item,
        timesBought: item.timesBought.size,
      }));

      // Sort by quantity (descending)
      sellingArray.sort((a, b) => b.totalQuantity - a.totalQuantity);

      // Top 5 selling products
      const topSelling = sellingArray.slice(0, 5);
      setTopSellingProducts(topSelling);

      // Least 5 selling products (products with sales but lowest quantities)
      const leastSelling = sellingArray.length > 5 
        ? sellingArray.slice(-5).reverse() 
        : [];
      setLeastSellingProducts(leastSelling);

      // Not selling products (products with no sales at all)
      const soldProductIds = new Set(sellingArray.map(p => p.productid));
      const notSelling = allProducts
        .filter(product => !soldProductIds.has(product.productid))
        .slice(0, 10) // Limit to 10 products
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
    fetchTopSellingProducts();
  }, []);

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
            <div className="dashboard-panel sales-summary">
              <div className="panel-header-with-action">
                <SalesSummaryDashboard />
              </div>
            </div>

            <div className="charts-section">
              <div className="dashboard-panel daily-sales">
                <h3>Daily Gross Sales</h3>
                <div className="panel-content">
                  <DailyGrossSales/>
                </div>
              </div>

              <div className="bottom-section">
                <div className="dashboard-panel monthly-expense">
                  <h3>Monthly Expense</h3>
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

                <div className="dashboard-panel top-selling">
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
                  {loading ? "Loading..." : user?.username || "No username found"}
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
                ⏻              </button>
            </div>

            <div className="notification-panel">
              <h3>Notifications</h3>
              <div className="activity-container">
                <Notifications />
              </div>
            </div>

            <div className="activity-panel">
              <h3>Recent Activity</h3>
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
    </div>
  );
};

export default Dashboard;