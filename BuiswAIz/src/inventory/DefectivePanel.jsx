// inventory/DefectivePanel.jsx
import React from "react";
import "../stylecss/DefectPanel.css";
import { toast } from "react-toastify";
import "react-toastify/dist/ReactToastify.css";

const DefectivePanel = ({ defectiveItems, user, loadDefectiveItems, onAddDefect }) => {

  const handleStatusChange = async (defectiveItemId, newStatus) => {
    if (!user) {
      toast.warning("User not loaded. Please wait...");
      return;
    }

    try {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/update-defect-status`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          defectiveItemId,
          newStatus,
          userId: user.userid,
        }),
      });

      const data = await res.json();

      if (!res.ok) {
        throw new Error(data.error || "Failed to update status");
      }

      loadDefectiveItems();
      toast.success(data.message || "Status updated successfully");
    } catch (err) {
      console.error("Update failed:", err);
      toast.error("Failed to update status. See console for details.");
    }
  };

  return (
    <div className="defective-panel">
      <div className="panel-title-row">
        <h3>Defective Items</h3>
        <button className="panel-action-button" onClick={onAddDefect}>+ Add Defect</button>
      </div>

      <div className="defective-container">
        {defectiveItems.length === 0 ? (
          <p className="no-low-stock">No defective items reported.</p>
        ) : (
          defectiveItems.map((item) => (
            <div key={item.defectiveitemid} className="defective-item">

              {/* Product Image */}
              {item.products?.image_url ? (
                <img
                  src={item.products.image_url}
                  alt={item.products.productname || "Product"}
                  className="defectimg-placeholder"
                />
              ) : (
                <div className="defectimg-placeholder" />
              )}

              {/* Product Info */}
              <div className="defective-details">
                <div className="defect-main">
                  <span className="defect-name">{item.products?.productname || "Unnamed"}</span>

                  {item.productcategory && (
                    <div className="variant-info">
                      <span>Color: {item.productcategory.color || "N/A"}</span>
                      <span>Size/Age: {item.productcategory.agesize || "N/A"}</span>
                    </div>
                  )}

                  <span className="Quantity">Quantity: {item.quantity} pcs</span>
                  <span className="ReportedDate">
                    Reported: {item.reporteddate ? new Date(item.reporteddate).toLocaleDateString() : "N/A"}
                  </span>
                  <span className="Description">{item.defectdescription}</span>
                </div>

                {/* Status Dropdown */}
                <div className="defect-status">
                  <select
                    className="status-dropdown"
                    value={item.status}
                    onChange={(e) => handleStatusChange(item.defectiveitemid, e.target.value)}
                  >
                    <option value="In-Process">In-Process</option>
                    <option value="Returned">Returned</option>
                  </select>
                </div>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
};

export default DefectivePanel;
