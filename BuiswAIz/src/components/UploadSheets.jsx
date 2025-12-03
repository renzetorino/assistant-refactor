// UploadSheets.jsx
import React, { useState, useMemo } from "react";
import * as XLSX from "xlsx";
import { toast } from "react-toastify";
import { validateSpreadsheetRows, uploadValidatedData } from "../services/supabaseUploader";
import "../stylecss/UploadSheets.css";

const REQUIRED_COLUMNS = [
  "orderid",
  "productname",
  "color",
  "agesize",
  "quantity",
  "unitprice",
  "subtotal",
  "amountpaid",
  "orderdate",
];

function normalizeHeader(h) {
  return String(h || "").trim().toLowerCase();
}

// ⬇️ Changed: make this a named export
export function downloadTemplate() {
  const headers = [
    "orderid",
    "orderdate",
    "productname",
    "color",
    "agesize",
    "quantity",
    "unitprice",
    "subtotal",
    "amountpaid",
  ];

  const sample = [
    "10001",
    "2025-10-04",
    "Basic Tee",
    "Black",
    "M",
    "2",
    "250",
    "500",
    "500"
  ];

  const hasXLSX = typeof window !== "undefined" && window.XLSX;

  if (hasXLSX) {
    const wsData = [headers, sample];
    const ws = window.XLSX.utils.aoa_to_sheet(wsData);
    const wb = window.XLSX.utils.book_new();
    window.XLSX.utils.book_append_sheet(wb, ws, "Sales Upload Template");

    const wbout = window.XLSX.write(wb, { bookType: "xlsx", type: "array" });
    const blob = new Blob([wbout], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });

    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "sales_upload_template.xlsx";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  } else {
    const rows = [headers, sample];
    const csv = rows.map(r =>
      r.map(v => {
        const s = String(v ?? "");
        if (/[",\n]/.test(s)) return `"${s.replace(/"/g, '""')}"`;
        return s;
      }).join(",")
    ).join("\n");

    const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "sales_upload_template.csv";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }
}

// Optional: keep a global for any legacy callers
if (typeof window !== "undefined") {
  window.downloadTemplate = downloadTemplate;
}

function UploadSheets() {
  const [rawRows, setRawRows] = useState([]);
  const [rows, setRows] = useState([]);
  const [validReport, setValidReport] = useState(null);
  const [loading, setLoading] = useState(false);
  const [fileName, setFileName] = useState("");
  const [warnings, setWarnings] = useState([]);

  const allValid = useMemo(() => {
    if (!validReport) return false;
    return validReport.rows.every(r => !r.errors || r.errors.length === 0) && validReport.groups.every(g => !g.errors || g.errors.length === 0);
  }, [validReport]);

  const handleFile = async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setFileName(file.name);
    setLoading(true);
    try {
      const data = await file.arrayBuffer();
      const wb = XLSX.read(data, { type: "array" });
      const ws = wb.Sheets[wb.SheetNames[0]];
      const json = XLSX.utils.sheet_to_json(ws, { header: 1, raw: true });
      if (!json.length) throw new Error("Empty sheet");

      const headers = json[0].map(normalizeHeader);
      const missing = REQUIRED_COLUMNS.filter(col => !headers.includes(col));
      if (missing.length) {
        toast.error(`Missing required columns: ${missing.join(", ")}`);
        setRawRows([]);
        setRows([]);
        setValidReport(null);
        setWarnings([]);
        return;
      }

      const idx = Object.fromEntries(headers.map((h, i) => [h, i]));
      const parsed = json.slice(1).filter(r => r && r.some(v => v !== undefined && v !== null && String(v).trim() !== "")).map((r, rowIndex) => ({
        __row: rowIndex + 2,
        orderid: r[idx.orderid],
        productname: r[idx.productname],
        color: r[idx.color],
        agesize: r[idx.agesize],
        quantity: r[idx.quantity],
        unitprice: r[idx.unitprice],
        subtotal: r[idx.subtotal],
        amountpaid: r[idx.amountpaid],
        orderdate: r[idx.orderdate],
      }));

      setRawRows(parsed);
      setRows(parsed);
      const report = await validateSpreadsheetRows(parsed);
      setValidReport(report);
      setWarnings(report.warnings || []);
      if (report.rows.some(r => r.errors?.length)) {
        toast.warn("Some rows need fixes. Please edit inline until all errors are resolved.");
      } else if (report.groups.some(g => g.errors?.length)) {
        toast.warn("Some order groups have issues. Please fix them.");
      } else {
        toast.success("Looks good! You can upload.");
      }
    } catch (err) {
      console.error(err);
      toast.error(err.message || "Failed to parse spreadsheet");
    } finally {
      setLoading(false);
    }
  };

  const onCellChange = async (rowIdx, key, value) => {
    const updated = rows.map((r, i) => (i === rowIdx ? { ...r, [key]: value } : r));
    setRows(updated);
    setLoading(true);
    try {
      const report = await validateSpreadsheetRows(updated);
      setValidReport(report);
      setWarnings(report.warnings || []);
    } catch (e) {
      console.error(e);
      toast.error("Validation failed while editing");
    } finally {
      setLoading(false);
    }
  };

  const upload = async () => {
    if (!validReport || !allValid) {
      toast.error("Please fix all errors before uploading.");
      return;
    }
    setLoading(true);
    try {
      const res = await uploadValidatedData(validReport);
      if (res.success) {
        toast.success("Upload complete!");
        setRawRows([]);
        setRows([]);
        setValidReport(null);
        setWarnings([]);
        setFileName("");
      } else {
        throw new Error(res.error?.message || "Upload failed");
      }
    } catch (e) {
      console.error(e);
      toast.error(e.message || "Upload failed");
    } finally {
      setLoading(false);
    }
  };

  const renderCell = (row, rowIdx, key) => {
    const value = row[key] ?? "";
    const hasError = !!validReport?.rows?.[rowIdx]?.errors?.some(err => err.field === key);
    return (
      <td key={key}>
        <input
          value={value}
          onChange={(e) => onCellChange(rowIdx, key, e.target.value)}
          className={hasError ? "border border-red-500" : "border border-gray-300"}
          style={{ padding: 10, minWidth: 120 }}
        />
      </td>
    );
  };

  return (
    <div style={{ display: "grid", gap: 12 }}>
      <div className="file-choose">
        <input type="file" accept=".xlsx,.xls,.csv" onChange={handleFile} disabled={loading} />
        {fileName && <small style={{ marginLeft: 8 }}>Loaded: {fileName}</small>}
      </div>

      {warnings?.length > 0 && (
        <div style={{ color: "#fff", background: "#da02027c", padding: 12, borderRadius: 8 }}>
          <strong>Warnings:</strong>
          <ul style={{ marginTop: 6 }}>
            {warnings.map((w, i) => (<li key={i}>• {w}</li>))}
          </ul>
        </div>
      )}

      {validReport && (
        <div style={{ overflowX: "auto" }}>
          <table className="min-w-full border-collapse">
            <thead>
              <tr>
                {REQUIRED_COLUMNS.map(h => (
                  <th key={h} className="text-left border-b p-2">{h}</th>
                ))}
                <th className="text-left border-b p-2">Errors</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, rowIdx) => (
                <tr key={rowIdx}>
                  {REQUIRED_COLUMNS.map(k => renderCell(row, rowIdx, k))}
                  <td style={{ color: "#dc2626" }}>
                    {validReport?.rows?.[rowIdx]?.errors?.map((e, i) => (
                      <div key={i}>• {e.message}</div>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <div style={{ marginTop: 12, display: "flex", gap: 12, alignItems: "center" }}>
            <button onClick={upload} disabled={loading || !allValid} className="btn btn-primary">
              {loading ? "Validating..." : "Upload"}
            </button>
            {!allValid && <span style={{ color: "#dc2626" }}>Fix errors to enable upload.</span>}
          </div>
        </div>
      )}
    </div>
  );
}

export default UploadSheets;
