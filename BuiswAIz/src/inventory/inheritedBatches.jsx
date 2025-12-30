import React, { useEffect, useState } from "react";
import { fetchInheritedBatches } from "../inventory/fetchRestockStorage";
import "../stylecss/inheritedBatches.css";

const InheritedBatches = ({ user }) => {
  const [inheritedBatches, setInheritedBatches] = useState([]);

  /* ===============================
     LOAD INHERITED BATCHES
  ================================ */
  const loadInheritedBatches = async () => {
    if (!user?.business_id) return;

    try {
      const data = await fetchInheritedBatches(user.business_id); // ✅ PASS BUSINESS ID
      setInheritedBatches(data);
    } catch (err) {
      console.error("Failed to fetch inherited batches:", err);
    }
  };

  /* ===============================
     AUTO REFRESH
  ================================ */
  useEffect(() => {
    if (!user?.business_id) return;

    loadInheritedBatches();

    const interval = setInterval(loadInheritedBatches, 5000);
    return () => clearInterval(interval);
  }, [user?.business_id]);

  return (
    <div className="inherited-batches-panel">
      <h3>Stock Batches</h3>

      <div className="restock-container">
        <table className="restock-table">
          <thead>
            <tr>
              <th>BatchCode</th>
              <th>Product</th>
              <th>Category</th>
              <th>Quantity</th>
              <th>Cost</th>
              <th>Price</th>
              <th>Supplier</th>
              <th>Date Received</th>
              <th>Date Inherited</th>
            </tr>
          </thead>

          <tbody>
            {inheritedBatches.map((batch) => {
              const variantName = `${batch.productcategory?.color || ""} ${batch.productcategory?.agesize || ""}`.trim();

              return (
                <tr key={batch.restockid}>
                  <td>{batch.batchCode || "N/A"}</td>
                  <td>{batch.products?.productname || "Unknown"}</td>
                  <td>{variantName}</td>
                  <td>{batch.new_stock}</td>
                  <td>{batch.new_cost}</td>
                  <td>{batch.new_price}</td>
                  <td>{batch.suppliers?.suppliername || "Unknown"}</td>
                  <td>
                    {batch.datereceived
                      ? new Date(batch.datereceived).toLocaleDateString()
                      : "-"}
                  </td>
                  <td>
                    {batch.dateInherited
                      ? new Date(batch.dateInherited).toLocaleDateString()
                      : "-"}
                  </td>
                </tr>
              );
            })}

            {inheritedBatches.length === 0 && (
              <tr>
                <td colSpan="9" style={{ textAlign: "center", opacity: 0.6 }}>
                  No inherited batches found
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default InheritedBatches;
