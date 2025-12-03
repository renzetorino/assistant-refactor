// ProductItems.jsx
import React, { useState, useEffect } from "react";
import { supabase } from "../supabase"; // adjust path if needed
import "../stylecss/ProductItems.css"; // optional CSS file
import ConfirmDeleteModalCategory from "./ConfirmationModals/ConfirmationDeleteCategory";

const ProductItems = ({ productId, productName, onBack, refreshTrigger }) => {
    const [items, setItems] = useState([]);
    const [loading, setLoading] = useState(false);
    const [deleteTarget, setDeleteTarget] = useState(null); // item to delete
    const [modalMessage, setModalMessage] = useState("");
    const [showModal, setShowModal] = useState(false);
    const [disableDelete, setDisableDelete] = useState(false);

    const loadItems = async () => {
        setLoading(true);
        const { data, error } = await supabase
        .from("productcategory")
        .select("productcategoryid, price, cost, color, agesize, currentstock, reorderpoint")
        .eq("productid", productId);

        if (error) {
        console.error("Error fetching product items:", error);
        } else {
        setItems(data || []);
        }
        setLoading(false);
    };

    useEffect(() => {
        loadItems();
    }, [productId, refreshTrigger]);

    const checkReferences = async (categoryId) => {
        const tableConfigs = [
            { table: "defectiveitems", column: "defectiveitemid" },
            { table: "restockstorage", column: "restockid" },
            { table: "orderitems", column: "orderitemid" }
        ];
        
        const results = await Promise.all(
            tableConfigs.map(({ table, column }) =>
                supabase.from(table).select(column).eq("productcategoryid", categoryId).limit(1)
            )
        );

        for (let i = 0; i < results.length; i++) {
            const { data, error } = results[i];
            if (error) {
                console.error(`Error checking ${tableConfigs[i].table}:`, error);
                continue;
            }
            if (data && data.length > 0) {
                return tableConfigs[i].table;
            }
        }

        return null;
    };


    // 🔹 Start delete process (open modal)
    const handleDeleteClick = async (item) => {
        const referencedIn = await checkReferences(item.productcategoryid);

        if (referencedIn) {
            setModalMessage(
                `This category cannot be deleted because it is reference to a transaction or stored within the storage`
            );
            setDeleteTarget(null);
            setDisableDelete(true);   // 🚫 block deletion
            setShowModal(true);
        } else {
            setModalMessage("Are you sure you want to delete this category?");
            setDeleteTarget(item);
            setDisableDelete(false);  // ✅ allow deletion
            setShowModal(true);
        }
    };

    // 🔹 Confirm delete
    const confirmDelete = async () => {
        if (!deleteTarget) {
            setShowModal(false);
            return;
        }

        try {
            // 1️⃣ Delete stock_setting row(s) associated with this category
            const { error: stockError } = await supabase
                .from("stock_setting")
                .delete()
                .eq("productcategoryid", deleteTarget.productcategoryid);

            if (stockError) {
                console.error("Error deleting stock settings:", stockError);
            }

            // 2️⃣ Delete the category itself
            const { error: categoryError } = await supabase
                .from("productcategory")
                .delete()
                .eq("productcategoryid", deleteTarget.productcategoryid);

            if (categoryError) {
                console.error("Error deleting item:", categoryError);
                alert("Failed to delete category.");
            } else {
                setItems((prev) =>
                    prev.filter((i) => i.productcategoryid !== deleteTarget.productcategoryid)
                );
            }
        } catch (err) {
            console.error(err);
            alert("Failed to delete category.");
        } finally {
            setShowModal(false);
            setDeleteTarget(null);
        }
    };

    return (
        <div className="product-items-panel">
        <h3>{productName}</h3>
        {loading ? (
            <p>Loading...</p>
        ) : items.length === 0 ? (
            <p>No product variants found.</p>
        ) : (
            <div className="product-items-grid">
            {items.map((item) => (
                <div key={item.productcategoryid} className="product-item-card">
                <div className="item-details">
                    <div><strong>Color:</strong> {item.color || "N/A"}</div>
                    <div><strong>Age/Size:</strong> {item.agesize || "N/A"}</div>
                    <div><strong>Price:</strong> ₱{item.price || 0}</div>
                    <div><strong>Cost:</strong> ₱{item.cost || 0}</div>
                    <div><strong>Stock:</strong> {item.currentstock || 0}</div>
                    <div><strong>Reorder:</strong> {item.reorderpoint || 0}</div>
                </div>
                <div className="item-actions">
                    <button
                    onClick={() => handleDeleteClick(item)}
                    className="delete-button"
                    >
                    🗑️
                    </button>
                </div>
                </div>
            ))}
            </div>
        )}

        {/* 🔹 Confirmation Modal */}
        <ConfirmDeleteModalCategory
            show={showModal}
            onClose={() => setShowModal(false)}
            onConfirm={confirmDelete}
            message={modalMessage}
            disableConfirm={disableDelete} 
        />
        </div>
    );
};

export default ProductItems;
