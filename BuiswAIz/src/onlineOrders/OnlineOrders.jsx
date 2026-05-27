import React, { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "react-toastify";
import { supabase } from "../supabase";
import "../stylecss/inventory.css";
import "../stylecss/Dashboard/Dashboard.css";
import "../stylecss/onlineOrders.css";
import {
  ONLINE_STATUSES,
  normalizeStatus,
  getNextStatus,
  toDbStatus,
  recordDeliveredOrderAsSale,
} from "./onlineOrderUtils";

const OnlineOrders = () => {
  const navigate = useNavigate();
  const [user, setUser] = useState(null);
  const [lineItems, setLineItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [searchTerm, setSearchTerm] = useState("");
  const [statusFilter, setStatusFilter] = useState("active");
  const [selectedOrderId, setSelectedOrderId] = useState(null);
  const [updating, setUpdating] = useState(false);
  const [businessProductIds, setBusinessProductIds] = useState([]);

  useEffect(() => {
    const getUser = async () => {
      const {
        data: { user: authUser },
        error,
      } = await supabase.auth.getUser();

      if (error || !authUser) {
        window.location.href = "/";
        return;
      }

      const { data: profile, error: profileError } = await supabase
        .from("systemuser")
        .select("*")
        .eq("userid", authUser.id)
        .single();

      if (profileError) {
        console.error("Error fetching user profile:", profileError);
        return;
      }

      setUser(profile);
    };

    getUser();
  }, []);

  const loadOnlineOrders = async () => {
    if (!user?.business_id) return;

    setLoading(true);
    try {
      const { data: businessProducts, error: productsError } = await supabase
        .from("products")
        .select("productid")
        .eq("businessid", user.business_id);

      if (productsError) throw productsError;

      const productIds = (businessProducts || []).map((p) => p.productid);
      setBusinessProductIds(productIds);
      if (productIds.length === 0) {
        setLineItems([]);
        return;
      }

      const { data, error } = await supabase
        .from("OnlineOrderStatus")
        .select(
          `
          id,
          product,
          productcategoryid,
          OrderStatus,
          quantity,
          unitprice,
          subtotal,
          OrderOLid,
          products ( productid, productname, image_url, businessid ),
          productcategory ( productcategoryid, color, agesize, currentstock, price ),
          OrderOnline ( * )
        `
        )
        .in("product", productIds)
        .order("OrderOLid", { ascending: false });

      if (error) throw error;
      const ownedLines = (data || []).filter(
        (row) => row.products?.businessid === user.business_id
      );
      setLineItems(ownedLines);
    } catch (err) {
      console.error("Error loading online orders:", err);
      toast.error(err.message || "Failed to load online orders");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!user) return;
    loadOnlineOrders();
  }, [user]);

  const groupedOrders = useMemo(() => {
    const map = new Map();

    for (const row of lineItems) {
      const orderId = row.OrderOLid;
      if (orderId == null) continue;

      if (!map.has(orderId)) {
        map.set(orderId, {
          orderId,
          lines: [],
          orderMeta: row.OrderOnline,
        });
      }
      map.get(orderId).lines.push(row);
    }

    return Array.from(map.values()).map((group) => {
      const statuses = group.lines.map((l) => normalizeStatus(l.OrderStatus));
      const status = statuses.includes("delivered")
        ? "delivered"
        : statuses[0] || "pending";
      const total = group.lines.reduce(
        (sum, l) => sum + Number(l.subtotal ?? l.unitprice * l.quantity ?? 0),
        0
      );
      const itemCount = group.lines.reduce((sum, l) => sum + (l.quantity || 0), 0);
      const productNames = [
        ...new Set(
          group.lines.map((l) => l.products?.productname || "Unknown").filter(Boolean)
        ),
      ];

      return {
        ...group,
        status,
        total,
        itemCount,
        productLabel:
          productNames.length > 2
            ? `${productNames.slice(0, 2).join(", ")} +${productNames.length - 2} more`
            : productNames.join(", "),
      };
    });
  }, [lineItems]);

  const matchesSearch = (order) =>
    String(order.orderId).includes(searchTerm) ||
    order.productLabel.toLowerCase().includes(searchTerm.toLowerCase());

  const activeOrders = useMemo(() => {
    return groupedOrders.filter((order) => {
      if (order.status === "delivered") return false;
      if (!matchesSearch(order)) return false;
      if (statusFilter === "active") return true;
      return order.status === statusFilter;
    });
  }, [groupedOrders, searchTerm, statusFilter]);

  const deliveredOrders = useMemo(() => {
    return groupedOrders
      .filter((order) => order.status === "delivered" && matchesSearch(order))
      .sort((a, b) => b.orderId - a.orderId);
  }, [groupedOrders, searchTerm]);

  const statusCounts = useMemo(() => {
    const counts = { pending: 0, preparing: 0, shipped: 0, delivered: 0 };
    for (const order of groupedOrders) {
      if (counts[order.status] !== undefined) counts[order.status] += 1;
    }
    return counts;
  }, [groupedOrders]);

  const selectedOrder = useMemo(
    () => groupedOrders.find((o) => o.orderId === selectedOrderId) || null,
    [groupedOrders, selectedOrderId]
  );

  const updateOrderStatus = async (orderId, newStatus) => {
    if (!user) return;

    const group = groupedOrders.find((o) => o.orderId === orderId);
    if (!group) return;

    const current = group.status;
    if (current === "delivered") {
      toast.warning("Delivered orders cannot be changed.");
      return;
    }

    const normalizedNew = normalizeStatus(newStatus);
    const next = getNextStatus(current);

    if (normalizedNew === "delivered") {
      if (current !== "shipped") {
        toast.warning("Mark the order as shipped before delivering.");
        return;
      }
    } else if (normalizedNew !== next) {
      toast.warning(`Move this order to "${toDbStatus(next)}" first.`);
      return;
    }

    const ownedLineIds = group.lines.map((line) => line.id).filter(Boolean);
    if (ownedLineIds.length === 0) {
      toast.error("No line items found for your business on this order.");
      return;
    }

    setUpdating(true);
    try {
      if (normalizedNew === "delivered") {
        const { data: alreadyDelivered, error: checkError } = await supabase
          .from("OnlineOrderStatus")
          .select("id")
          .in("id", ownedLineIds)
          .ilike("OrderStatus", "delivered")
          .limit(1);

        if (checkError) throw checkError;
        if (alreadyDelivered?.length) {
          toast.info("Your items on this order are already delivered.");
          await loadOnlineOrders();
          return;
        }

        await recordDeliveredOrderAsSale({
          lineItems: group.lines,
          user,
          businessId: user.business_id,
        });
      }

      // Only update line items belonging to this business (not other sellers on same OrderOLid)
      let updateQuery = supabase
        .from("OnlineOrderStatus")
        .update({ OrderStatus: toDbStatus(normalizedNew) })
        .in("id", ownedLineIds);

      if (businessProductIds.length > 0) {
        updateQuery = updateQuery.in("product", businessProductIds);
      }

      const { error } = await updateQuery;

      if (error) throw error;

      toast.success(
        normalizedNew === "delivered"
          ? "Order delivered and recorded in Sales."
          : `Order #${orderId} updated to ${toDbStatus(normalizedNew)}.`
      );

      await loadOnlineOrders();
      if (normalizedNew === "delivered") {
        setSelectedOrderId(null);
      }
    } catch (err) {
      console.error("Status update failed:", err);
      toast.error(err.message || "Failed to update order status");
    } finally {
      setUpdating(false);
    }
  };

  const renderStatusActions = (order) => {
    if (!order) return null;

    const status = order.status;
    if (status === "delivered") {
      return (
        <p className="locked-note">
          This order is delivered and recorded in Sales. Status cannot be changed.
        </p>
      );
    }

    const next = getNextStatus(status);
    const isDeliver = next === "delivered";

    return (
      <div className="status-actions">
        {next && (
          <button
            type="button"
            className={`status-btn${isDeliver ? " deliver" : ""}`}
            disabled={updating}
            onClick={() => updateOrderStatus(order.orderId, next)}
          >
            {isDeliver ? "Mark as Delivered" : `Mark as ${toDbStatus(next)}`}
          </button>
        )}
      </div>
    );
  };

  return (
    <div className="inventory-page online-orders-page">
      <header className="header-bar">
        <h1 className="header-title">Bake-keri</h1>
      </header>

      <div className="main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              {/* <li onClick={() => navigate("/expenses")}>Expenses</li> */}
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              {/* <li onClick={() => navigate("/pos")}>Point of Sales</li> */}
              <li className="active">Online Orders</li>
              {/* <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li> */}
            </ul>
          </div>
        </aside>

        <div className="I-main-content">
          <div className="product-panel">
            <div className="panel-header">
              <div className="header-left-dash">
                <h2 className="panel-title">Online Orders</h2>
              </div>
              <div className="panel-actions">
                <select
                  className="inventory-search"
                  style={{ width: "160px" }}
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value)}
                >
                  <option value="active">Active (not delivered)</option>
                  <option value="pending">Pending only</option>
                  <option value="preparing">Preparing only</option>
                  <option value="shipped">Shipped only</option>
                  <option value="all">All active</option>
                </select>
                <input
                  className="inventory-search"
                  type="text"
                  placeholder="Search order or product"
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                />
                <button
                  type="button"
                  className="panel-action-button"
                  onClick={loadOnlineOrders}
                  disabled={loading}
                >
                  Refresh
                </button>
              </div>
            </div>

            <div className="inventory-container active-orders-container">
              <h3 className="orders-section-title">Active orders</h3>
              {loading ? (
                <p className="empty-hint">Loading orders...</p>
              ) : activeOrders.length === 0 ? (
                <p className="empty-hint">No active online orders match this filter.</p>
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>Order #</th>
                      <th>Products</th>
                      <th>Items</th>
                      <th>Total</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {activeOrders.map((order) => (
                      <tr
                        key={`active-${order.orderId}`}
                        className={selectedOrderId === order.orderId ? "selected" : ""}
                        onClick={() => setSelectedOrderId(order.orderId)}
                        style={{ cursor: "pointer" }}
                      >
                        <td>#{order.orderId}</td>
                        <td>{order.productLabel || "—"}</td>
                        <td>{order.itemCount}</td>
                        <td>₱{order.total.toFixed(2)}</td>
                        <td>
                          <span className={`status-badge status-${order.status}`}>
                            {toDbStatus(order.status)}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>

            <div className="delivered-orders-panel">
              <h3 className="orders-section-title">
                Delivered orders
                <span className="delivered-count">{deliveredOrders.length}</span>
              </h3>
              <div className="delivered-orders-container">
                {loading ? (
                  <p className="empty-hint">Loading...</p>
                ) : deliveredOrders.length === 0 ? (
                  <p className="empty-hint">No delivered orders yet.</p>
                ) : (
                  <table>
                    <thead>
                      <tr>
                        <th>Order #</th>
                        <th>Products</th>
                        <th>Items</th>
                        <th>Total</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {deliveredOrders.map((order) => (
                        <tr
                          key={`delivered-${order.orderId}`}
                          className={selectedOrderId === order.orderId ? "selected" : ""}
                          onClick={() => setSelectedOrderId(order.orderId)}
                          style={{ cursor: "pointer" }}
                        >
                          <td>#{order.orderId}</td>
                          <td>{order.productLabel || "—"}</td>
                          <td>{order.itemCount}</td>
                          <td>₱{order.total.toFixed(2)}</td>
                          <td>
                            <span className="status-badge status-delivered">Delivered</span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </div>
          </div>

          <div className="I-right-panel">
            <div className="I-user-info-card">
              <div className="I-user-left">
                <div className="I-user-avatar" />
                <div className="I-user-username">
                  {user ? user.username : "Loading..."}
                </div>
              </div>
              <button
                type="button"
                className="logout-button"
                onClick={async () => {
                  await supabase.auth.signOut();
                  localStorage.removeItem("userProfile");
                  localStorage.removeItem("lastActive");
                  window.location.href = "/login";
                }}
              >
                ⏻
              </button>
            </div>

            <div className="summary-card">
              <h3 style={{ fontSize: "15px", fontWeight: 600, marginBottom: "10px" }}>
                Order summary
              </h3>
              {ONLINE_STATUSES.map((s) => (
                <div key={s.key} className="summary-row">
                  <span className={`status-badge status-${s.key}`}>{s.label}</span>
                  <strong>{statusCounts[s.key]}</strong>
                </div>
              ))}
            </div>

            <div className="order-detail-panel">
              {!selectedOrder ? (
                <p className="empty-hint">
                  Select an active or delivered order to view details.
                </p>
              ) : (
                <>
                  <h3>Order #{selectedOrder.orderId} (your items)</h3>
                  <div className="detail-line">
                    <span>Status</span>
                    <span className={`status-badge status-${selectedOrder.status}`}>
                      {toDbStatus(selectedOrder.status)}
                    </span>
                  </div>
                  <div className="detail-line">
                    <span>Total</span>
                    <span>₱{selectedOrder.total.toFixed(2)}</span>
                  </div>

                  <h3 style={{ marginTop: "16px" }}>Line items</h3>
                  {selectedOrder.lines.map((line) => (
                    <div key={line.id} className="detail-line">
                      <span>
                        {line.products?.productname || "Product"}{" "}
                        {line.productcategory?.color || line.productcategory?.agesize
                          ? `(${[line.productcategory?.color, line.productcategory?.agesize]
                              .filter(Boolean)
                              .join(" • ")})`
                          : ""}{" "}
                        × {line.quantity}
                      </span>
                      <span>₱{Number(line.subtotal ?? 0).toFixed(2)}</span>
                    </div>
                  ))}

                  {renderStatusActions(selectedOrder)}
                </>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default OnlineOrders;
