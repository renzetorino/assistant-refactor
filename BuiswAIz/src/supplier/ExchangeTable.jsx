import React, { useEffect, useState } from "react";
import { supabase } from "../supabase";
import "../stylecss/ExchangeTable.css";

const ExchangeTable = ({ onClose, user }) => {
  const [exchanges, setExchanges] = useState([]);
  const [filteredExchanges, setFilteredExchanges] = useState([]);
  const [loading, setLoading] = useState(true);
  const [updating, setUpdating] = useState(false);
  const [filterStatus, setFilterStatus] = useState("All");

  // Priority sorting rule
  const priority = { Confirmed: 1, Pending: 2, Completed: 3 };

  // Fetch all exchanges
  const fetchExchanges = async () => {
    console.log("ExchangeTable modal opened");
    try {
      const { data, error } = await supabase
        .from("productExchange")
        .select(`
          exchangeid,
          supplier_id,
          old_product,
          old_product_category,
          new_product,
          new_product_category,
          reason,
          requested_at,
          confirmed_at,
          completed_at,
          quantity,
          status,
          suppliers (suppliername),
          old_product_ref:old_product (productname),
          new_product_ref:new_product (productname),
          old_cat_ref:old_product_category (color, agesize),
          new_cat_ref:new_product_category (color, agesize)
        `)
        .order("exchangeid", { ascending: false });

      if (error) throw error;

      const sorted = (data || []).sort((a, b) => {
        const aPriority = priority[a.status] || 99;
        const bPriority = priority[b.status] || 99;
        return aPriority - bPriority;
      });

      setExchanges(sorted);
      setFilteredExchanges(sorted);
    } catch (err) {
      console.error("Error fetching product exchanges:", err);
    } finally {
      setLoading(false);
    }
  };

  // Handle filtering
  const handleFilterChange = (e) => {
    const status = e.target.value;
    setFilterStatus(status);

    if (status === "All") {
      setFilteredExchanges(exchanges);
    } else {
      const filtered = exchanges.filter((ex) => ex.status === status);
      setFilteredExchanges(filtered);
    }
  };

  // Handle completing an exchange
  const handleComplete = async (exchange) => {
    if (updating) return;
    setUpdating(true);

    try {
      console.log("Completing exchange:", exchange.exchangeid);

      // 1️⃣ Fetch current stock of the new product category
      const { data: categoryData, error: categoryError } = await supabase
        .from("productcategory")
        .select("currentstock")
        .eq("productcategoryid", exchange.new_product_category)
        .single();

      if (categoryError) throw categoryError;

      const updatedStock =
        Number(categoryData.currentstock || 0) + Number(exchange.quantity || 0);

      // 2️⃣ Update the product category's current stock
      const { error: stockUpdateError } = await supabase
        .from("productcategory")
        .update({ currentstock: updatedStock })
        .eq("productcategoryid", exchange.new_product_category);

      if (stockUpdateError) throw stockUpdateError;

      // 3️⃣ Update the productExchange record
      const { error: exchangeUpdateError } = await supabase
        .from("productExchange")
        .update({
          status: "Completed",
          completed_at: new Date().toISOString().split("T")[0],
        })
        .eq("exchangeid", exchange.exchangeid);

      if (exchangeUpdateError) throw exchangeUpdateError;

      // 4️⃣ Log the completion activity
      if (user?.userid) {
        await supabase.from("activitylog").insert([
          {
            action_type: "complete_exchange",
            action_desc: `Completed product exchange for ${exchange.new_product_ref?.productname || "a product"} (+${exchange.quantity} stock)`,
            done_user: user.userid,
          },
        ]);
      }

      // 5️⃣ Update local UI state instantly
      setExchanges((prev) => {
        const updated = prev.map((ex) =>
          ex.exchangeid === exchange.exchangeid
            ? { ...ex, status: "Completed", completed_at: new Date().toISOString() }
            : ex
        );

        const sorted = updated.sort((a, b) => {
          const aPriority = priority[a.status] || 99;
          const bPriority = priority[b.status] || 99;
          return aPriority - bPriority;
        });

        // Re-apply filter after updating
        if (filterStatus === "All") setFilteredExchanges(sorted);
        else setFilteredExchanges(sorted.filter((ex) => ex.status === filterStatus));

        return sorted;
      });

      alert("Exchange marked as completed and stock updated successfully!");
    } catch (err) {
      console.error("Error completing exchange:", err);
      alert("Failed to complete exchange. Check console for details.");
    } finally {
      setUpdating(false);
    }
  };

  useEffect(() => {
    fetchExchanges();
  }, []);

  return (
    <div className="exchange-modal-overlay">
      <div className="exchange-modal-box">
        <h2>Product Exchange Records</h2>
        <button className="close-btn" onClick={onClose}>×</button>

        {/* ✅ Filter Dropdown */}
        <div className="filter-section">
          <label htmlFor="filter-status">Filter by Status: </label>
          <select
            id="filter-status"
            value={filterStatus}
            onChange={handleFilterChange}
          >
            <option value="All">All</option>
            <option value="Confirmed">Confirmed</option>
            <option value="Pending">Pending</option>
            <option value="Completed">Completed</option>
          </select>
        </div>

        {loading ? (
          <p>Loading exchanges...</p>
        ) : filteredExchanges.length === 0 ? (
          <p>No product exchanges found.</p>
        ) : (
          <div className="exchange-table-wrapper">
            <table className="exchange-table">
              <thead>
                <tr>
                  <th>Supplier</th>
                  <th>Old Product</th>
                  <th>Old Category</th>
                  <th>New Product</th>
                  <th>New Category</th>
                  <th>Quantity</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {filteredExchanges.map((ex) => (
                  <tr key={ex.exchangeid}>
                    <td>{ex.suppliers?.suppliername || "Unknown"}</td>
                    <td>{ex.old_product_ref?.productname || "N/A"}</td>
                    <td>
                      {ex.old_cat_ref?.color || ""} {ex.old_cat_ref?.agesize || ""}
                    </td>
                    <td>{ex.new_product_ref?.productname || "N/A"}</td>
                    <td>
                      {ex.new_cat_ref?.color || ""} {ex.new_cat_ref?.agesize || ""}
                    </td>
                    <td>{ex.quantity}</td>
                    <td className={`status-${ex.status?.toLowerCase()}`}>
                      {ex.status}
                    </td>
                    <td>
                      {ex.status === "Confirmed" ? (
                        <button
                          className="complete-btn"
                          disabled={updating}
                          onClick={() => handleComplete(ex)}
                        >
                          {updating ? "Processing..." : "Complete"}
                        </button>
                      ) : (
                        "-"
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};

export default ExchangeTable;
