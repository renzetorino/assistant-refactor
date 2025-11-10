// UploadInventory.jsx
import React, { useState, useMemo, useEffect } from "react";
import * as XLSX from "xlsx";
import { toast } from "react-toastify";
// ⬇️ IMPORTANT: You must create these new service functions!
import { validateInventoryRows, uploadInventoryData } from "../services/supabaseUploader"; 
import "../stylecss/UploadSheets.css";

// ⬇️ Changed: New column definitions for inventory
const REQUIRED_COLUMNS = [
  "productname",
  "suppliername",
  "cost",
  "price",
  "stock",
];

const OPTIONAL_COLUMNS = [
  "description",
  "color",
  "agesize",
  "reorderpoint",
];

// ⬇️ Changed: New synonyms for inventory fields
const FIELD_SYNONYMS = {
  productname: ["product","item","item name","product name","sku name","sku"],
  suppliername: ["supplier", "vendor", "supplier name"],
  description: ["desc", "product description", "details"],
  color: ["colour","variant color","color/variant","shade"],
  agesize: ["age/size","size","age size","dimension"],
  cost: ["cost price", "unit cost", "purchase price", "your cost"],
  price: ["selling price", "unit price", "retail price", "srp", "price each"],
  stock: ["quantity", "qty", "on hand", "current stock", "inventory", "stock level", "qty.", "pcs", "pieces"],
  reorderpoint: ["reorder point", "reorder level", "min stock", "minimum stock"]
};


function coerceNumber(val) {
  if (val == null) return null;
  if (typeof val === "number") return isFinite(val) ? val : null;
  const s = String(val).trim();
  if (!s) return null;
  // Strip common currency/commas
  const cleaned = s.replace(/[₱$,]/g, "").replace(/\s/g, "");
  const n = Number(cleaned);
  return isFinite(n) ? n : null;
}

// ⬇️ Changed: Use a different key for inventory mapping
function saveMap(map){ try{ localStorage.setItem("upload_inventory_map_v1", JSON.stringify(map)); }catch{} }
function loadMap(){ try{ return JSON.parse(localStorage.getItem("upload_inventory_map_v1")||"null"); }catch{ return null; } }


function normalizeHeader(h) {
  return String(h || "").trim().toLowerCase();
}

// ⬇️ Changed: New template function for inventory
export function downloadInventoryTemplate() {
  const headers = [
    "productname",
    "suppliername",
    "description",
    "color",
    "agesize",
    "cost",
    "price",
    "stock",
    "reorderpoint",
  ];

  const sample = [
    "Kids T-Shirt",
    "Main Supplier Inc.",
    "A comfy cotton t-shirt.",
    "Blue",
    "Age 6",
    "150",
    "300",
    "50",
    "10"
  ];
  
  // ⬇️ Add a second variant for the same product to show grouping
  const sample2 = [
    "Kids T-Shirt",
    "Main Supplier Inc.",
    "A comfy cotton t-shirt.",
    "Red",
    "Age 8",
    "150",
    "300",
    "40",
    "10"
  ];
  
  // ⬇️ Add a different product
  const sample3 = [
    "Baby Onesie",
    "BabyWear Co.",
    "Soft organic cotton.",
    "White",
    "0-3 Mos",
    "200",
    "450",
    "30",
    "5"
  ];

  const hasXLSX = typeof window !== "undefined" && window.XLSX;

  if (hasXLSX) {
    const wsData = [headers, sample, sample2, sample3]; // ⬇️ Use all samples
    const ws = window.XLSX.utils.aoa_to_sheet(wsData);
    const wb = window.XLSX.utils.book_new();
    window.XLSX.utils.book_append_sheet(wb, ws, "Inventory Upload Template");

    const wbout = window.XLSX.write(wb, { bookType: "xlsx", type: "array" });
    const blob = new Blob([wbout], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });

    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "inventory_upload_template.xlsx";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  } else {
    // CSV fallback (simplified for brevity)
    const rows = [headers, sample, sample2, sample3];
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
    a.download = "inventory_upload_template.csv";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }
}

// Levenshtein and similarity scoring functions (unchanged)
function levenshtein(a, b) {
  a = (a||""); b = (b||"");
  const m = Array.from({length:a.length+1}, (_,i)=>[i]);
  for (let j=0;j<=b.length;j++) m[0][j]=j;
  for (let i=1;i<=a.length;i++){
    for (let j=1;j<=b.length;j++){
      const cost = a[i-1]===b[j-1] ? 0 : 1;
      m[i][j] = Math.min(m[i-1][j]+1, m[i][j-1]+1, m[i-1][j-1]+cost);
    }
  }
  return m[a.length][b.length];
}

function scoreSimilarity(requiredKey, header) {
  const r = normalizeHeader(requiredKey);
  const h = normalizeHeader(header);
  if (!h) return 0;
  if (h === r) return 1.0;
  if (FIELD_SYNONYMS[r]?.includes(h)) return 0.95;
  if (h.includes(r) || r.includes(h)) return 0.85;
  const dist = levenshtein(r, h);
  const maxLen = Math.max(r.length, h.length) || 1;
  const sim = 1 - dist / maxLen;
  return Math.max(0, Math.min(0.8, sim));
}

function guessHeaderMap(sheetHeaders) {
  const headers = sheetHeaders.map(normalizeHeader);
  const unique = Array.from(new Set(headers));
  const picks = {};
  [...REQUIRED_COLUMNS, ...OPTIONAL_COLUMNS].forEach(req => {
    let best = null, bestScore = 0;
    unique.forEach(h => {
      const s = scoreSimilarity(req, h);
      if (s > bestScore) { best = h; bestScore = s; }
    });
    picks[req] = best || null;
  });
  return picks;
}


// ⬇️ Changed: Renamed component to UploadInventory
function UploadInventory() {
  const [rawRows, setRawRows] = useState([]);
  const [rows, setRows] = useState([]);
  const [validReport, setValidReport] = useState(null);
  const [loading, setLoading] = useState(false);
  const [fileName, setFileName] = useState("");
  const [warnings, setWarnings] = useState([]);
  const [detectedHeaders, setDetectedHeaders] = useState([]);
  const [headerMap, setHeaderMap] = useState(null);
  const [stage, setStage] = useState("idle"); 
  const [lastSheet, setLastSheet] = useState(null);

  useEffect(() => {
    const prior = loadMap(); // ⬅️ Uses new loadMap key
    if (prior) {
      const normalized = Object.fromEntries(Object.entries(prior).map(([k,v]) => [k, normalizeHeader(v)]));
      setHeaderMap(m => ({ ...(m || {}), ...normalized }));
    }
  }, []);


  const allValid = useMemo(() => {
    if (!validReport) return false;
    // ⬇️ Changed: Check product groups as well as individual rows
    const rowsOk = Array.isArray(validReport.rows)
      ? validReport.rows.every(r => !r.errors || r.errors.length === 0)
      : true;
    const groupsOk = Array.isArray(validReport.groups)
      ? validReport.groups.every(g => !g.errors || g.errors.length === 0)
      : true;
    return rowsOk && groupsOk;
  }, [validReport]);

  const handleFile = async (e) => {
    // ... (This function is unchanged, just parses the sheet)
    const file = e.target.files?.[0];
    if (!file) return;
    setFileName(file.name);
    setLoading(true);
    try {
      const data = await file.arrayBuffer();
      const wb = XLSX.read(data, { type: "array" });
      const ws = wb.Sheets[wb.SheetNames[0]];
      const json = XLSX.utils.sheet_to_json(ws, { header: 1, raw: true });
      setLastSheet(json);
      if (!json.length) throw new Error("Empty sheet");

      const sheetHeaders = json[0];
      setDetectedHeaders(sheetHeaders);

      const initial = guessHeaderMap(sheetHeaders);
      setHeaderMap(initial);
      setValidReport(null);
      setWarnings([]);
      setRawRows([]);
      setRows([]);
      setStage("map");  

    } catch (err) {
      console.error(err);
      toast.error(err.message || "Failed to parse spreadsheet");
    } finally {
      setLoading(false);
    }
  };


  const applyMapping = async (sheet) => {
    if (!headerMap || !detectedHeaders?.length) return;
    const unmappedReq = REQUIRED_COLUMNS.filter(k => !headerMap[k]);
    if (unmappedReq.length) {
      toast.error(`Please map required fields: ${unmappedReq.join(", ")}`);
      return;
    }
    const idx = Object.fromEntries(detectedHeaders.map((h,i) => [normalizeHeader(h), i]));
    const pick = (row, key) => {
      const m = headerMap[key];
      if (!m) return null;
      const i = idx[m];
      return i == null ? null : (row[i] === "" ? null : row[i]); // ⬅️ Treat empty strings as null
    };
    
    // ⬇️ Changed: Parsing logic for inventory fields
    const body = sheet.slice(1).filter(r => r && r.some(v => v != null && String(v).trim() !== ""));
    const parsed = body.map((r, rowIndex) => {
      return {
        __row: rowIndex + 2, // Spreadsheet row number
        productname: pick(r, "productname"),
        suppliername: pick(r, "suppliername"),
        description: pick(r, "description"),
        color: pick(r, "color"),
        agesize: pick(r, "agesize"),
        cost: coerceNumber(pick(r, "cost")),
        price: coerceNumber(pick(r, "price")),
        stock: coerceNumber(pick(r, "stock")),
        reorderpoint: coerceNumber(pick(r, "reorderpoint")),
      };
    });
    setRawRows(parsed);
    setRows(parsed);
    saveMap(headerMap); // ⬅️ Uses new saveMap key
    setLoading(true);
    try{
      // ⬇️ IMPORTANT: Calls the new validation service
      const report = await validateInventoryRows(parsed); 
      setValidReport(report);
      setWarnings(report.warnings || []);
      setStage("ready");
      if (report.rows?.some(r => r.errors?.length)) {
        toast.warn("Some rows need fixes. Please edit inline until all errors are resolved.");
      } else if (report.groups?.some(g => g.errors?.length)) {
        // ⬇️ Changed: Check for group errors (e.g., "Product X has different suppliers")
        toast.warn("Some product groups have issues. Please fix them.");
      } else {
        toast.success("Looks good! You can upload.");
      }
    } finally {
      setLoading(false);
    }
  };

  const onCellChange = async (rowIdx, key, value) => {
    let v = value;
    // ⬇️ Changed: Numeric fields for inventory
    if (["cost", "price", "stock", "reorderpoint"].includes(key)) {
        v = coerceNumber(value);
    }
    const updated = rows.map((r, i) => (i === rowIdx ? { ...r, [key]: v } : r));
    setRows(updated);
    setLoading(true);
    try {
      // ⬇️ IMPORTANT: Calls the new validation service
      const report = await validateInventoryRows(updated); 
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
      // ⬇️ IMPORTANT: Calls the new upload service
      const res = await uploadInventoryData(validReport); 
      if (res.success) {
        toast.success("Inventory upload complete!");
        setRawRows([]);
        setRows([]);
        setValidReport(null);
        setWarnings([]);
        setFileName("");
        setStage("idle"); // ⬅️ Reset stage
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
    
    // ⬇️ Check for group errors related to this row's product
    const productName = row.productname;
    // ⬇️ FIX: Normalize product name to lowercase for matching group key
    const normalizedProductName = String(productName || '').toLowerCase(); 
    const hasGroupError = !!validReport?.groups?.find(g => 
        g.key === normalizedProductName && // <-- Use normalized name
        g.errors?.some(err => err.field === key)
    );
    
    return (
      <td key={key}>
        <input
          value={value}
          onChange={(e) => onCellChange(rowIdx, key, e.target.value)}
          // ⬇️ Show error if this cell is bad OR if its group has an error for this field
          className={(hasError || hasGroupError) ? "border border-red-500" : "border border-gray-300"}
          style={{ padding: 10, minWidth: 120 }}
        />
      </td>
    );
  };
  
  // ⬇️ Get all columns for rendering
  const allColumns = [...REQUIRED_COLUMNS, ...OPTIONAL_COLUMNS];

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

      {headerMap && detectedHeaders?.length > 0 && (
        <div style={{ overflowX: "auto" }}>
            <div className="card" style={{padding:12}}>
              <strong>Review column mapping</strong>
              {/* ⬇️ Changed: Render loop for new columns */}
              {allColumns.map(field => (
                <div key={field} style={{display:"flex", gap:8, alignItems:"center", marginTop:8}}>
                  <span style={{width:140}}>{field}{OPTIONAL_COLUMNS.includes(field) && " (optional)"}</span>
                  <select
                    value={headerMap[field] || ""}
                    onChange={(e)=> setHeaderMap(m => ({...m, [field]: normalizeHeader(e.target.value)}))}
                  >
                    <option value="">— Unmapped —</option>
                    {Array.from(new Set(detectedHeaders.map(normalizeHeader))).map(h => (
                      <option key={h} value={h}>{h}</option>
                    ))}
                  </select>
                </div>
              ))}
              <div style={{marginTop:10, display:"flex", gap:8}}>
                <button className="btn btn-primary" onClick={() => { saveMap(headerMap); toast.success("Mapping saved"); }}>Save mapping</button>
                <button className="btn" onClick={() => { const m = loadMap(); if (m){ setHeaderMap(Object.fromEntries(Object.entries(m).map(([k,v])=>[k, normalizeHeader(v)]))); toast.info("Loaded last mapping"); }}}>Use my last mapping</button>
                <button
                 className="btn"
                 onClick={() => { if (lastSheet) applyMapping(lastSheet); }}
                 disabled={!lastSheet || loading}
               >
                 Apply mapping
               </button>
              </div>
            </div>

          {stage !== "idle" && (
          <table className="min-w-full border-collapse">
            <thead>
              <tr>
                {/* ⬇️ Changed: Table headers for new columns */}
                {allColumns.map(h => (
                  <th key={h} className="text-left border-b p-2">
                    {h}{OPTIONAL_COLUMNS.includes(h) && <span style={{marginLeft:6, fontSize:12, opacity:.7}}>(optional)</span>}
                  </th>
                ))}
                <th className="text-left border-b p-2">Errors</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, rowIdx) => {
                // ⬇️ FIX: Normalize product name here to match group key
                const normalizedProductName = String(row.productname || '').toLowerCase();
                // ⬇️ FIX: Normalize findIndex check
                const isFirstRowInGroup = rows.findIndex(r => String(r.productname || '').toLowerCase() === normalizedProductName) === rowIdx;
                
                return (
                  <tr key={rowIdx}>
                    {/* ⬇️ Changed: Render cells for new columns */}
                    {allColumns.map(k => renderCell(row, rowIdx, k))}
                    <td style={{ color: "#dc2626" }}>
                      {/* Show row-specific errors */}
                      {validReport?.rows?.[rowIdx]?.errors?.map((e, i) => (
                        <div key={i}>• {e.message}</div>
                      ))}
                      {/* Show group-level errors on the first row of that group */}
                      {validReport?.groups?.find(g => g.key === normalizedProductName)?.errors?.map((e, i) => (
                          (isFirstRowInGroup) && // <-- Use the calculated variable
                          <div key={`g-${i}`}>• (Group) {e.message}</div>
                      ))}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
            )}

          <div style={{ marginTop: 12, display: "flex", gap: 12, alignItems: "center" }}>
            <button
              onClick={upload}
              disabled={loading || !allValid || stage !== "ready"}
              className="btn btn-primary"
              title={stage !== "ready" ? "Apply mapping and fix any errors before uploading" : ""}
            >
              {loading ? (stage === "ready" ? "Uploading..." : "Validating...") : "Upload"}
            </button>
            {stage !== "idle" && stage !== "ready" && (
              <span style={{ color: "#64748b" }}>Apply mapping to validate data.</span>
            )}
            {stage === "ready" && !allValid && (
              <span style={{ color: "#dc2626" }}>Fix errors to enable upload.</span>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export default UploadInventory;