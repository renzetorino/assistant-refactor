import React from "react";

export default function InventoryWindow({ items = [] }) {
  const rows = items.length ? items : demoRows;
  return (
    <div className="pw-section">
      <div className="pw-toolbar">
        <input className="pw-input" placeholder="Search product or SKU" />
        <div className="pw-btns">
          <button className="pw-btn primary">Receive Stock</button>
          <button className="pw-btn">Adjust Qty</button>
          <button className="pw-btn">Export CSV</button>
        </div>
      </div>

      <div className="pw-table-wrap">
        <table className="pw-table">
          <thead>
            <tr>
              <th>Product</th>
              <th>SKU</th>
              <th>On Hand</th>
              <th>Reorder Point</th>
              <th>Cost</th>
              <th>Supplier</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.sku} className={r.onHand <= r.reorderPoint ? "low" : ""}>
                <td>
                  <div className="pw-prod">
                    <div className="pw-thumb" />
                    <div>
                      <div className="pw-name">{r.name}</div>
                      <div className="pw-sub">{r.category}</div>
                    </div>
                  </div>
                </td>
                <td>{r.sku}</td>
                <td>{r.onHand}</td>
                <td>{r.reorderPoint}</td>
                <td>₱ {r.cost.toLocaleString()}</td>
                <td>{r.supplier}</td>
                <td><button className="pw-row-btn">Details</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

const demoRows = [
  { name: "Airism Shirt – Classic", category: "Apparel", sku: "AIR-CLS-001", onHand: 24, reorderPoint: 30, cost: 195, supplier: "Uni Co." },
  { name: "Renzchi Short", category: "Apparel", sku: "REN-SHO-002", onHand: 60, reorderPoint: 25, cost: 92, supplier: "Renzchi" },
  { name: "Carly Cap", category: "Accessories", sku: "CRL-CAP-003", onHand: 12, reorderPoint: 20, cost: 112, supplier: "Carly Corp" },
];
