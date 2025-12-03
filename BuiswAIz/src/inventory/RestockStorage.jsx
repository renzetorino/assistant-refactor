import React, { useEffect, useState } from "react";
import { fetchActiveBatches } from "../inventory/fetchRestockStorage";
import { supabase } from "../supabase";
import "../stylecss/RestockStorage.css";
import RestockProduct from "./RestockProduct";

const RestockStorage = ({ onClose, user }) => {
    const [storageData, setStorageData] = useState([]);
    const [showAdd, setShowAdd] = useState(false);
    
    useEffect(() => {
        loadActiveBatches();
    }, []);

    const loadActiveBatches = async () => {
        try {
            const data = await fetchActiveBatches();
            setStorageData(data);
        } catch (error) {
            console.error("Failed to fetch active batches:", error);
        }
    };

    const handleConsumeRestock = async (restock) => {
        const productCategoryId = restock.productcategory?.productcategoryid;
        const currentStock = restock.productcategory?.currentstock ?? 0;
        const variantName = `${restock.productcategory?.color || ""}, ${restock.productcategory?.agesize || ""}`.trim();

        if (!productCategoryId) {
            alert("Cannot inherit: Variant not found");
            return;
        }

        if (currentStock > 0) {
            alert("Cannot inherit: Product still has stock");
            return;
        }

        try {
            // Update productcategory stock
            const { error: updateError } = await supabase
                .from("productcategory")
                .update({
                    currentstock: restock.new_stock,
                    updatedstock: new Date(),
                    cost: restock.new_cost,
                    price: restock.new_price
                })
                .eq("productcategoryid", productCategoryId);

            if (updateError) {
                console.error("Error updating stock:", updateError);
                return alert("Failed to inherit batch");
            }
            
            // Mark batch as inherited
            const { error: batchError } = await supabase
                .from("restockstorage")
                .update({ dateInherited: new Date() })
                .eq("restockid", restock.restockid);

            if (batchError) console.error(batchError);

            // Log activity
            await supabase.from("activitylog").insert({
                action_desc: `Inherited batch for ${restock.products?.productname} (${variantName})`,
                done_user: user?.userid || null
            });

            // Reload active batches
            loadActiveBatches(); 
        } catch (err) {
            console.error("Unexpected error:", err);
        }
    };

    return (
        <div className="modal-overlay">
            <div className="modal-window">
                {!showAdd ? (
                <>
                    <h2>Restock Storage</h2>
                    <button onClick={() => setShowAdd(true)} className="restock-btn">Restock+</button>
                    <button onClick={onClose} className="close-btn">X</button>
                    <div className="restock-panel">
                        <div className="restock-container">
                        <table className="restock-table">
                            <thead>
                                <tr>
                                    <th>BatchCode</th>
                                    <th>Product</th>
                                    <th>Variant</th>
                                    <th>Quantity</th>
                                    <th>Cost</th>
                                    <th>Price</th>
                                    <th>Supplier</th>
                                    <th>Date Received</th>
                                    <th>Stock</th>
                                    <th>Reorder Point</th>
                                    <th>Action</th>
                                </tr>
                            </thead>
                            <tbody>
                                {storageData.map((restock) => {
                                    const current = restock.productcategory?.currentstock ?? 0;
                                    const reorder = restock.productcategory?.reorderpoint ?? 0;
                                    const isLow = current < reorder;
                                    const variantName = `${restock.productcategory?.color || ""} ${restock.productcategory?.agesize || ""}`.trim();

                                    return (
                                        <tr key={restock.restockid}>
                                            <td>{restock.batchCode || "N/A"}</td>
                                            <td>{restock.products?.productname || "Unknown"}</td>
                                            <td>{variantName}</td>
                                            <td>{restock.new_stock}</td>
                                            <td>{restock.new_cost}</td>
                                            <td>{restock.new_price}</td>
                                            <td>{restock.suppliers?.suppliername || "Unknown"}</td>
                                            <td>{restock.datereceived ? new Date(restock.datereceived).toLocaleDateString() : "-"}</td>
                                            <td>
                                                <span className={isLow ? "stock-low" : "stock-ok"}>{current}</span>
                                            </td>
                                            <td>{reorder}</td>
                                            <td>
                                                <button
                                                    className="restock-btn"
                                                    onClick={() => handleConsumeRestock(restock)}
                                                    disabled={current > 0}>
                                                    Inherit
                                                </button>
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                        </div>
                    </div>
                </>
                ) : (
                <RestockProduct
                    onClose={() => setShowAdd(false)}
                    onSuccess={() => {
                        setShowAdd(false);
                        loadActiveBatches();
                    }}
                    user={user}
                />
                )}
            </div>
        </div>
    );
};

export default RestockStorage;
