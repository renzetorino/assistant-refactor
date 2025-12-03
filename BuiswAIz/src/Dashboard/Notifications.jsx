import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { supabase } from '../supabase';
import '../stylecss/Dashboard/Notifications.css';

const Notifications = () => {
  const navigate = useNavigate();
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    fetchNotifications();
  }, []);

  const fetchNotifications = async () => {
    try {
      setLoading(true);
      const allNotifications = [];
         const nowPH = new Date(
          new Date().toLocaleString("en-US", { timeZone: "Asia/Manila" })
        );

      // 1. INVENTORY: Low stock (currentstock < reorderpoint)
      try {
        const { data: lowStockItems, error: stockError } = await supabase
          .from('productcategory')
          .select(`
            productcategoryid,
            productid,
            currentstock,
            reorderpoint,
            updatedstock,
            color,
            agesize,
            products (productname)
          `);

        if (stockError) {
          if (!stockError.message.includes('does not exist')) throw stockError;
        } else if (lowStockItems?.length) {
          const filteredLowStock = lowStockItems.filter(
            (item) => Number(item.currentstock ?? 0) < Number(item.reorderpoint ?? 0)
          );

          filteredLowStock.forEach((item, index) => {
            const variants = [];
            if (item.color?.trim()) variants.push(`C: ${item.color}`);
            if (item.agesize?.trim()) variants.push(`S: ${item.agesize}`);
            const variantInfo = variants.length ? variants.join(', ') : null;

            allNotifications.push({
              id: `stock-${item.productcategoryid}-${index}-${Date.now()}`,
              type: 'low-stock',
              title: 'Low Stock Alert',
              message: `${item.products?.productname || 'Unknown Product'}`,
              variantInfo,
              details: `Stock: ${item.currentstock}`,
              priority: 'high',
              timestamp: item.updatedstock ? new Date(item.updatedstock) : new Date(),
              navigationPath: '/inventory',
            });
          });
        }
      } catch {
        /* continue without low stock */
      }

      // 2. SALES: Incomplete orders
      try {
        const { data: incompleteOrders, error: ordersError } = await supabase
          .from('orders')
          .select('orderid, orderdate, orderstatus')
          .eq('orderstatus', 'INCOMPLETE')
          .order('orderdate', { ascending: false })
          .limit(10);

        if (ordersError) {
          if (!ordersError.message.includes('does not exist')) throw ordersError;
        } else if (incompleteOrders?.length) {
          incompleteOrders.forEach((order, index) => {
            allNotifications.push({
              id: `order-${order.orderid}-${index}-${Date.now()}`,
              type: 'incomplete-order',
              title: 'Incomplete Order',
              message: `Order ID: ${order.orderid}`,
              details: `Order Date: ${new Date(order.orderdate).toLocaleDateString()}`,
              priority: 'medium',
              timestamp: new Date(order.orderdate),
              navigationPath: '/TablePage',
            });
          });
        }
      } catch {
        /* continue without orders */
      }

      // 2.5 PURCHASE ORDERS
      try {
        const { data: purchaseOrders, error: purchaseError } = await supabase
          .from('purchase_orders')
          .select(`
          purchaseorderid,
          order_qty,
          unit_cost,
          status,
          confirmed_at,
          created_at,               
          products (productname),
          productcategory (color, agesize),
          suppliers (suppliername)
                `)
          .in('status', ['Confirmed', 'Pending'])
          .order('confirmed_at', { ascending: false })
          .limit(10);

        if (!purchaseError && purchaseOrders?.length) {
          purchaseOrders.forEach((order, index) => {
            const variants = [];
            if (order.productcategory?.color?.trim()) variants.push(`C: ${order.productcategory.color}`);
            if (order.productcategory?.agesize?.trim()) variants.push(`S: ${order.productcategory.agesize}`);
            const variantInfo = variants.length ? variants.join(', ') : null;

            allNotifications.push({
              id: `purchase-${order.purchaseorderid}-${index}-${Date.now()}`,
              type: 'purchase-order',
              title: 'Purchase Order',
              message: `${order.products?.productname || 'Unknown Product'}`,
              variantInfo,
              details: `Status: ${order.status} | Supplier: ${order.suppliers?.suppliername || 'Unknown'} | Qty: ${order.order_qty}`,
              priority: 'medium',
              timestamp:
                order.confirmed_at
                  ? new Date(order.confirmed_at)           // Confirmed → use confirmed_at
                  : (order.created_at ? new Date(order.created_at) : nowPH), // Pending → use created_at; NEVER “now”
              navigationPath: '/supplier'
            });
          });
        }
      } catch {}

      // 3. INVENTORY: Defective items
      try {
        const { data: defectiveItems, error: defectiveError } = await supabase
          .from('defectiveitems')
          .select(`
            productid,
            reporteddate,
            status,
            products (
              productname,
              productcategory (
                color,
                agesize
              )
            )
          `)
          .order('reporteddate', { ascending: false })
          .limit(10);

        if (defectiveError) {
          if (!defectiveError.message.includes('does not exist')) throw defectiveError;
        } else if (defectiveItems?.length) {
          defectiveItems.forEach((item, index) => {
            const productCategories = item.products?.productcategory || [];
            let variantInfo = null;

            if (Array.isArray(productCategories) && productCategories.length > 0) {
              const variants = [];
              productCategories.forEach((category) => {
                const vs = [];
                if (category.color?.trim()) vs.push(`C: ${category.color}`);
                if (category.agesize?.trim()) vs.push(`S: ${category.agesize}`);
                if (vs.length) variants.push(vs.join(', '));
              });
              if (variants.length) variantInfo = variants.join(' | ');
            }

            allNotifications.push({
              id: `defective-${item.productid}-${item.reporteddate}-${index}-${Date.now()}`,
              type: 'defective-item',
              title: 'Defective Item Report',
              message: `${item.products?.productname || 'Unknown Product'}`,
              variantInfo,
              details: `Status: ${item.status} | Reported: ${new Date(item.reporteddate).toLocaleDateString()}`,
              priority: 'high',
              timestamp: new Date(item.reporteddate),
              navigationPath: '/inventory',
            });
          });
        }
      } catch {
        /* continue without defective items */
      }

      // 4. BUDGET: Over budget / threshold alerts
      try {
        const THRESHOLD_PCT = 80;
        const DEBUG_BUDGET = true;
        const FORCE_BUDGET_TEST = false;

        const now = new Date();
        const y = now.getFullYear();
        const m = String(now.getMonth() + 1).padStart(2, '0');
        const start = `${y}-${m}-01`;
        const lastDayNum = new Date(y, now.getMonth() + 1, 0).getDate();
        const end = `${y}-${m}-${String(lastDayNum).padStart(2, '0')}`;

        const { data: budgets, error: budgetErr } = await supabase
          .from('budget')
          .select('id, month_year, monthly_budget_amount, created_at')
          .order('month_year', { ascending: false })
          .limit(24);
        if (budgetErr) throw budgetErr;

        const currentMonthBudget = (budgets || []).find((b) => {
          if (!b?.month_year) return false;
          const d = new Date(b.month_year);
          return d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth();
        });

        const { data: monthExpenses, error: expErr } = await supabase
          .from('expenses')
          .select('amount, occurred_on')
          .gte('occurred_on', start)
          .lte('occurred_on', end);
        if (expErr) throw expErr;

        const spent = (monthExpenses || []).reduce((sum, e) => {
          const val = Number(e?.amount ?? 0);
          return Number.isFinite(val) ? sum + val : sum;
        }, 0);
        const allocated = Number(currentMonthBudget?.monthly_budget_amount ?? 0);
        const usedPct = allocated > 0 ? Math.round((spent / allocated) * 100) : 0;

        // Compute effective timestamp
        const nowTs = new Date();

        // 1) Detect if there is any expense for "today" (Asia/Manila)
        const todayPH = new Date().toLocaleDateString('en-CA', { timeZone: 'Asia/Manila' }); // YYYY-MM-DD
        const hasExpenseToday = (monthExpenses || []).some(e => String(e?.occurred_on) === todayPH);

        // 2) Find the latest expense date (still useful if there’s no expense today)
        let latestExpenseTs = null;
        if (monthExpenses?.length) {
          const latest = monthExpenses
            .map(e => e?.occurred_on)
            .filter(Boolean)
            .map(d => {
              const [yy, mm, dd] = String(d).split('-').map(n => parseInt(n, 10));
              // Keep noon for historical days
              return new Date(yy, (mm || 1) - 1, dd || 1, 12, 0, 0, 0);
            })
            .sort((a, b) => b - a)[0];
          latestExpenseTs = latest || null;
        }

        const createdTs = currentMonthBudget?.created_at ? new Date(currentMonthBudget.created_at) : null;
        const clampToNow = (d) => (d && d > nowTs ? nowTs : d);

        // If there is an expense dated today (PH), show it as "now" to feel immediate.
        // Otherwise, fall back to the latest expense noon, or budget created_at, or now.
        const effectiveTs = hasExpenseToday
          ? nowTs
          : (clampToNow(latestExpenseTs) || clampToNow(createdTs) || nowTs);


        if (DEBUG_BUDGET) {
          console.log('[BUDGET] spent:', spent, 'allocated:', allocated, 'usedPct:', usedPct);
          console.log('[BUDGET] effectiveTs:', effectiveTs);
        }

        if (FORCE_BUDGET_TEST) {
          allNotifications.push({
            id: `budget-test-${Date.now()}`,
            type: 'budget',
            title: 'Budget (TEST CARD)',
            message: now.toLocaleString(undefined, { month: 'long', year: 'numeric' }),
            details: `Used ${spent} of ${allocated} (${usedPct}%)`,
            priority: 'medium',
            timestamp: new Date(),
            navigationPath: '/expenses',
          });
        }

        if (currentMonthBudget && allocated > 0) {
          const overBudget = usedPct >= 100;
          const crossedThreshold = !overBudget && usedPct >= THRESHOLD_PCT;

          if (overBudget || crossedThreshold) {
            const prettyMoney = (v) =>
              Number(v).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });

            allNotifications.push({
              id: `budget-${currentMonthBudget.id}-${Date.now()}`,
              type: 'budget',
              title: overBudget ? 'Budget Exceeded' : 'Budget Threshold Reached',
              message: now.toLocaleString(undefined, { month: 'long', year: 'numeric' }),
              details: `Used ${prettyMoney(spent)} of ${prettyMoney(allocated)} (${usedPct}%)`,
              priority: overBudget ? 'high' : 'medium',
              timestamp: effectiveTs,
              navigationPath: '/expenses',
            });
          }
        }
      } catch (e) {
        console.error('Budget fetch error:', e);
        allNotifications.push({
          id: `budget-error-${Date.now()}`,
          type: 'debug',
          title: 'Budget Debug',
          message: 'Error while fetching budget/expenses',
          details: String(e?.message || e),
          priority: 'low',
          timestamp: new Date(),
        });
      }


      // 5. PLANNED PAYMENTS: due/advance reminders (respects `notify`)
      try {
        // Map enum values to day offsets relative to due_date
        // Map your enum -> day offsets relative to due_date
        const OFFSETS = {
          none: null,
          due_date: 0,
          one_day_before: -1,
          three_days_before: -3,
          week_before: -7,
        };


          const DAYMS = 24 * 60 * 60 * 1000;
          const todayPH = new Date().toLocaleDateString("en-CA", { timeZone: "Asia/Manila" });
          const todayDatePH = new Date(`${todayPH}T00:00:00+08:00`);

          const { data: plans, error: ppErr } = await supabase
            .from("planned_payments")
            .select("id, name, amount, due_date, notify, frequency, completed_at, expense_id, category_id, contact_id")
            .neq("notify", "none")
            .not("due_date", "is", null);

          if (ppErr) throw ppErr;

          (plans || [])
            .filter(pp => !pp.completed_at && !pp.expense_id)
            .forEach((pp, index) => {
              const offset = OFFSETS[String(pp.notify)] ?? null;
              const dueISO = String(pp.due_date || "");
              if (offset === null || !/^\d{4}-\d{2}-\d{2}$/.test(dueISO)) return;

              const [yy, mm, dd] = dueISO.split("-").map(n => parseInt(n, 10));
              const duePH = new Date(`${yy}-${String(mm).padStart(2,"0")}-${String(dd).padStart(2,"0")}T00:00:00+08:00`);
              const windowStartPH = new Date(duePH.getTime() + offset * DAYMS);

              // 🔁 Show reminder on ANY day between alert start and the due date (inclusive)
              if (todayDatePH >= windowStartPH && todayDatePH <= duePH) {
                const daysLeft = Math.max(0, Math.ceil((duePH - todayDatePH) / DAYMS));
                const isToday = daysLeft === 0;

                allNotifications.push({
                  id: `pp-window-${pp.id}-${pp.notify}-${index}-${todayPH}`,
                  type: "planned-payment",
                  title: isToday ? "Payment Due Today" : "Upcoming Payment",
                  message: pp.name || "Planned payment",
                  details: `Amount: ₱${Number(pp.amount || 0).toFixed(2)} • Due: ${dueISO}` + (isToday ? "" : ` • In ${daysLeft} day(s)`),
                  priority: isToday ? "high" : "medium",
                  timestamp: nowPH,
                  navigationPath: "/PlannedPaymentsPage",
                });
              }

              // ⏰ Overdue (only after due date, still open)
              if (todayDatePH > duePH) {
                allNotifications.push({
                  id: `pp-overdue-${pp.id}-${index}-${todayPH}`,
                  type: "planned-payment-overdue",
                  title: "Overdue Payment",
                  message: pp.name || "Planned payment",
                  details: `Amount: ₱${Number(pp.amount || 0).toFixed(2)} • Due: ${dueISO}`,
                  priority: "high",
                  timestamp: duePH,
                  navigationPath: "/PlannedPaymentsPage",
                });
              }
            });
      } catch (e) {
        console.error("Planned payments fetch error:", e);
      }


      // Sort notifications
      allNotifications.sort((a, b) => {
        const timeDiff = new Date(b.timestamp) - new Date(a.timestamp);
        if (Math.abs(timeDiff) < 60000) {
          const order = { high: 3, medium: 2, low: 1 };
          return (order[b.priority] ?? 0) - (order[a.priority] ?? 0);
        }
        return timeDiff;
      });

      setNotifications(allNotifications);
      setError(null);
    } catch (error) {
      console.error('Error fetching notifications:', error);
      setError(`Failed to load notifications: ${error.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleNotificationClick = (notification) => {
    if (notification.navigationPath) {
      navigate(notification.navigationPath);
    }
  };

  const getPriorityClass = (priority) => `notification-item priority-${priority}`;

  function formatTimestamp(timestamp) {
    if (!timestamp) return "";
    const nowPH = new Date(new Date().toLocaleString("en-US", { timeZone: "Asia/Manila" }));
    const ts = new Date(timestamp);

    const isSameDay =
      nowPH.getFullYear() === ts.getFullYear() &&
      nowPH.getMonth() === ts.getMonth() &&
      nowPH.getDate() === ts.getDate();

    if (isSameDay) return "Today";

    const diffMs = nowPH - ts;
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays < 1) return "Today";
    if (diffDays === 1) return "Yesterday";
    return `${diffDays} days ago`;
  }



  if (loading) {
    return (
      <div className="notifications-container">
        <div className="notifications-loading">
          <p>Loading notifications...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="notifications-container">
        <div className="notifications-error">
          <p>{error}</p>
          <button onClick={fetchNotifications} className="retry-button">Retry</button>
        </div>
      </div>
    );
  }

  return (
    <div className="notifications-container">
      <div className="notifications-list">
        {notifications.length === 0 ? (
          <div className="no-notifications">
            <div className="no-notifications-icon">✓</div>
            <p>All good! No notifications at the moment.</p>
          </div>
        ) : (
          notifications.map((notification) => (
            <div
              key={notification.id}
              className={`${getPriorityClass(notification.priority)} clickable-notification`}
              onClick={() => handleNotificationClick(notification)}
              style={{ cursor: 'pointer' }}
            >
              <div className="notification-content">
                <div className="notification-header">
                  <span className="notification-title">{notification.title}</span>
                  <div className="notification-message">{notification.message}</div>
                  {notification.variantInfo && (
                    <div
                      className="notification-variant-info"
                      style={{ fontSize: '12px', color: '#666', fontStyle: 'italic', marginTop: '2px' }}
                    >
                      {notification.variantInfo}
                    </div>
                  )}
                </div>
                {notification.details && (
                  <div className="notification-details">{notification.details}</div>
                )}
                <span className="notification-time">
                  {formatTimestamp(notification.timestamp)}
                </span>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
};

export default Notifications;
