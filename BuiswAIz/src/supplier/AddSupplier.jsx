import React, { useState } from "react";
import "../stylecss/AddSupplier.css";
import { supabase } from "../supabase";

const AddSupplier = ({ onClose, user }) => {
  const [formData, setFormData] = useState({
    suppliername: "",
    contactperson: "",
    phonenumber: "",
    supplieremail: "",
    address: "",
    supplierstatus: "Active",
  });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [formError, setFormError] = useState("");

  const handleChange = (e) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
  };

  const handleSubmit = async () => {
    setFormError("");

    const validateForm = () => {
      const requiredFields = [
        "suppliername",
        "contactperson",
        "phonenumber",
        "supplieremail",
        "address",
        "supplierstatus",
      ];

      // ✅ Clean phone number before validation
      const cleanPhone = formData.phonenumber.replace(/-/g, "");

      // Check if any field is empty or invalid
      const isEmpty = requiredFields.some((field) => {
        const value = formData[field];
        if (value === undefined || value === null) return true;
        if (typeof value === "string" && value.trim() === "") return true;
        return false;
      });

      if (isEmpty) {
        setFormError("Please fill in all required fields.");
        return false;
      }

      if (!/\S+@\S+\.\S+/.test(formData.supplieremail)) {
        setFormError("Invalid email format.");
        return false;
      }

      if (!/^\d{7,}$/.test(cleanPhone)) {
        setFormError("Phone number must only contain numbers with at least 7 digits.");
        return false;
      }

      return true;
    };

    if (!validateForm()) return;
    if (isSubmitting) return;

    setIsSubmitting(true);

    // ✅ Insert cleaned phone number (no dashes) to Supabase
    const { error } = await supabase.from("suppliers").insert({
      ...formData,
      phonenumber: formData.phonenumber.replace(/-/g, ""),
    });

    if (error) {
      console.error("Insert failed:", error);
      alert("Failed to add supplier.");
    } else {
      if (user) {
        await supabase.from("activitylog").insert([
          {
            action_type: "add_supplier",
            action_desc: `added ${formData.suppliername} to the supplier list`,
            done_user: user.userid,
          },
        ]);
      }
      onClose();
    }
  };


  return (
    <div className="modal-overlay">
      <div className="AddSmodal-content slide-up">
        {/* Header */}
        <div className="modal-header">
          <button className="back-btn" onClick={onClose}>←</button>
          <h2>Supplier</h2>
          <div className="modal-actions">
            <button className="create-btn" onClick={handleSubmit} disabled={isSubmitting}>{isSubmitting ? "Creating..." : "Create Supplier"}</button>
          </div>
        </div>

        {/* Body */}
        <div className="modal-body">
          <div className="supplier-fields">
            <label>Supplier Name</label>
            <input
              name="suppliername"
              className="product-name-input"
              placeholder="Supplier Name"
              value={formData.suppliername}
              onChange={handleChange}
            />

            <div className="two-cols">
              <input
                name="contactperson"
                placeholder="Contact Person"
                value={formData.contactperson}
                onChange={handleChange}
              />
              <input
                type="tel"
                name="phonenumber"
                placeholder="Phone Number"
                value={formData.phonenumber}
                onChange={(e) => {
                  let value = e.target.value.replace(/\D/g, ""); // keep only digits

                  // ✅ Format for PH mobile numbers (11 digits): 0927-538-7129
                  if (value.length > 4 && value.length <= 7) {
                    value = `${value.slice(0, 4)}-${value.slice(4)}`;
                  } else if (value.length > 7) {
                    value = `${value.slice(0, 4)}-${value.slice(4, 7)}-${value.slice(7, 11)}`;
                  }

                  handleChange({ target: { name: "phonenumber", value } });
                }}
              />
            </div>

            <div className="two-cols">
              <input
                name="supplieremail"
                placeholder="Email Address"
                value={formData.supplieremail}
                onChange={handleChange}
              />
              <input
                name="address"
                placeholder="Address"
                value={formData.address}
                onChange={handleChange}
              />
            </div>

            <div className="two-cols">
              <select
                name="supplierstatus"
                value={formData.supplierstatus}
                onChange={handleChange}
              >
                <option value="Active">Active</option>
                <option value="Inactive">Inactive</option>
              </select>
            </div>
          </div>
          {formError && (
            <div className="supplyForm-warning">
                {formError}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default AddSupplier;
