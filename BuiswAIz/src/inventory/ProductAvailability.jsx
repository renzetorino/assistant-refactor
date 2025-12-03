import React, { useState } from "react";
import { formatDistanceToNow, parseISO } from "date-fns";
import { ToastContainer, toast } from "react-toastify";
import "react-toastify/dist/ReactToastify.css";
import "../stylecss/ProductAvailability.css";

const ProductAvailability = ({ lowStockProducts }) => {
  // ✅ Track loading per product category
  const [loadingIds, setLoadingIds] = useState([]);

  const handleReorder = async (category) => {
    const id = category.productcategoryid;
    setLoadingIds((prev) => [...prev, id]); // mark as loading

    try {
      const res = await fetch(`${import.meta.env.VITE_BACKEND_URL}/api/reorder`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          productid: category.productid,
          productcategoryid: category.productcategoryid,
          supplierid: category.product?.supplierid,
        }),
      });

      const data = await res.json();
      if (!res.ok) return toast.error(data.error || "Failed to reorder");

      toast.success(data.message || "Reorder placed!");
    } catch (err) {
      console.error(err);
      toast.error("Failed to reorder");
    } finally {
      setLoadingIds((prev) => prev.filter((i) => i !== id)); // remove from loading
    }
  };

  return (
    <div className="availability-panel">
      <h3>Product Availability</h3>
      <div className="availability-container">
        {lowStockProducts.length === 0 ? (
          <p className="no-low-stock">✅ All items are sufficiently stocked.</p>
        ) : (
          lowStockProducts
            .sort(
              (a, b) =>
                b.reorderpoint - b.currentstock - (a.reorderpoint - a.currentstock)
            )
            .map((category, i) => {
              const product = category.product || {};
              const categoryLabel =
                [category.color, category.agesize].filter(Boolean).join(" / ") ||
                "N/A";
              const deficit = Math.max(category.reorderpoint - category.currentstock, 0);

              let severity = "ok";
              if (category.currentstock === 0) severity = "critical";
              else if (category.currentstock < category.reorderpoint / 2) severity = "urgent";
              else if (category.currentstock < category.reorderpoint) severity = "warning";

              const lastUpdated = category.updatedstock
                ? formatDistanceToNow(parseISO(category.updatedstock), { addSuffix: true })
                : "No recent updates";

              // ✅ Determine if this category is currently loading
              const isLoading = loadingIds.includes(category.productcategoryid);

              return (
                <div
                  key={category.productcategoryid || i}
                  className={`availability-item severity-${severity}`}
                >
                  {product.image_url ? (
                    <img
                      src={product.image_url}
                      alt={product.productname || "Product"}
                      className="I-product-thumbnail"
                    />
                  ) : (
                    <div className="img-placeholder">📦</div>
                  )}

                  <div className="availability-details">
                    <span className="name">
                      {product.productname || "Unnamed Product"}{" "}
                      <span className="category-label">({categoryLabel})</span>
                    </span>

                    <div className="stock-meter">
                      <div
                        className={`stock-fill severity-${severity}`}
                        style={{
                          width: `${Math.min(
                            (category.currentstock / category.reorderpoint) * 100,
                            100
                          )}%`,
                        }}
                      ></div>
                    </div>

                    <div className="stock-summary">
                      <span className="stock">{category.currentstock} pcs left</span>
                      <span className="reorder-point">Reorder at {category.reorderpoint}</span>
                    </div>

                    <div className="extra-info">
                      <span className="deficit">Deficit: {deficit > 0 ? deficit : 0}</span>
                      <span className="time">Updated {lastUpdated}</span>
                    </div>

                    <button
                      className="reorder-btn"
                      onClick={() => handleReorder(category)}
                      disabled={isLoading} // disable button while loading
                    >
                      {isLoading ? "Reordering..." : "Reorder"} {/* show loading */}
                    </button>
                  </div>
                </div>
              );
            })
        )}
      </div>
      <ToastContainer position="top-right" autoClose={3000} />
    </div>
  );
};

export default ProductAvailability;
