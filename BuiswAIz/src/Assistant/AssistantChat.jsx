import React, { useRef, useEffect, useState } from "react";
import "../stylecss/AssistantChat.css";

/**
 * Props:
 * - messages: Array<{ id: string, role: 'user'|'assistant', text?: string, payload?: any }>
 * - onSend: (text: string) => void
 * - onAction: (actionId: string, message: any) => void
 * - loading: boolean
 * - prompts: Array<{ id, text, category }>
 */
export default function AssistantChat({ 
  messages = [], 
  onSend, 
  onAction, 
  loading,
  prompts = []
}) {
  const [input, setInput] = useState("");
  const listRef = useRef(null);
  const [showPrompts, setShowPrompts] = useState(false);

  useEffect(() => {
    if (listRef.current) {
      listRef.current.scrollTop = listRef.current.scrollHeight;
    }
  }, [messages, loading]);

  const send = () => {
    const trimmed = input.trim();
    if (!trimmed) return;
    onSend?.(trimmed);
    setInput("");
  };

  const handleKey = (e) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      send();
    }
  };

  return (
    <div className="asst-wrap">
      {/* Header */}
      <div className="asst-header">
        <div className="asst-brand">
          <div className="asst-logo">B</div>
          <div className="asst-title">
            <div className="asst-name">BuiswAIz</div>
            <div className="asst-sub">AI Assistant</div>
          </div>
        </div>
      </div>

      {/* Prompts Suggestions Section */}
      {showPrompts && prompts && prompts.length > 0 && (
        <div className="asst-prompts-section">
          <div className="asst-prompts-header">
            <span className="asst-prompts-title">💡 Suggested Questions</span>
            <button 
              className="asst-prompts-toggle"
              onClick={() => setShowPrompts(false)}
              title="Hide suggestions"
            >
              ✕
            </button>
          </div>
          <div className="asst-prompts-grid">
            {prompts.map((prompt) => (
              <div
                key={prompt.id}
                className="asst-prompt-card"
                title="Suggested question"
              >
                <div className="asst-prompt-text">{prompt.text}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Show prompts button when hidden */}
      {!showPrompts && (
        <div className="asst-prompts-show">
          <button 
            className="asst-prompts-show-btn"
            onClick={() => setShowPrompts(true)}
          >
            💡 Show Suggested Prompts
          </button>
        </div>
      )}

      {/* Chat list */}
      <div className="asst-chat-list" ref={listRef}>
        {messages.map((m) => (
          <ChatBubble
            key={m.id}
            role={m.role}
            text={m.text}
            payload={m.payload}
            actions={m.payload?.actions || m.actions}
            onAction={(id) => onAction?.(id, m)}
          />
        ))}
        {loading && (
          <div className="asst-row asst-assistant">
            <div className="asst-bubble asst-bubble-assistant">
              <span className="asst-typing">
                <i className="dot" /> <i className="dot" /> <i className="dot" />
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Input bar */}
      <div className="asst-input-bar">
        <textarea
          className="asst-input"
          placeholder="Type your prompt (e.g., “Create a sales report for September 2025”)"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKey}
          rows={1}
        />
        <button className="asst-send" onClick={send} disabled={loading}>
          {loading ? "…" : "Submit"}
        </button>
      </div>
    </div>
  );
}

function ChatBubble({ role, text, payload }) {
  const isUser = role === "user";
  return (
    <div className={`asst-row ${isUser ? "asst-user" : "asst-assistant"}`}>
      <div className={`asst-bubble ${isUser ? "asst-bubble-user" : "asst-bubble-assistant"}`}>
        {/* Plain text */}
        {text && <p className="asst-text">{text}</p>}

        {/* Report payload → render inside resizable frame */}
        {!isUser && payload && (payload.report_title || payload.kpis || payload.cards || payload.charts) && (
          <ResizableBubble baseWidth={1080}>
            <MiniReport payload={payload} />
          </ResizableBubble>
        )}

        {/* Narrative bullets if present (kept) */}
        {!isUser && Array.isArray(payload?.narrative) && payload.narrative.length > 0 && (
          <ul className="asst-narr">
            {payload.narrative.map((n, i) => (
              <li key={i}>{n}</li>
            ))}
          </ul>
        )}

        {/* Narrative (string) via notes.narrative */}
        {!isUser && typeof payload?.notes?.narrative === "string" && payload.notes.narrative.trim() && (
          <p className="asst-narr-text">{payload.notes.narrative}</p>
        )}

        {/* Action buttons removed as requested */}
      </div>
    </div>
  );
}

/* ===== NEW: Generic resizable wrapper for large report content ===== */
function ResizableBubble({ baseWidth = 1080, minScale = 0.6, maxScale = 1, children }) {
  const frameRef = useRef(null);
  const [scale, setScale] = useState(1);
  const [mode, _setMode] = useState("fit"); // setter unused; underscore ok for eslint

  // Auto-fit to available width
  useEffect(() => {
    const el = frameRef.current;
    if (!el) return;

    function recompute() {
      if (mode !== "fit") return;
      const avail = el.clientWidth - 4; // small padding allowance
      const s = Math.min(maxScale, Math.max(minScale, avail / baseWidth));
      setScale(Number.isFinite(s) ? s : 1);
    }

    recompute();
    const obs = new ResizeObserver(recompute);
    obs.observe(el);
    return () => obs.disconnect();
  }, [baseWidth, minScale, maxScale, mode]);

  // Manual presets
  useEffect(() => {
    if (mode === "fit") return;
    const map = { "100": 1, "90": 0.9, "80": 0.8, "70": 0.7 };
    const s = map[mode] ?? 1;
    setScale(s);
  }, [mode]);

  return (
    <div className="bubble-scale-frame" ref={frameRef} style={{ "--base-width": `${baseWidth}px`, "--scale": scale }}>
      <div className="bubble-scale-toolbar">
      </div>
      <div className="bubble-scale-inner">
        {children}
      </div>
    </div>
  );
}

/** Minimal report renderer (kept) */
function MiniReport({ payload }) {
  const { report_title, period, kpis, cards, charts } = payload;

  return (
    <div className="mini-report">
      {/* Title & period chip */}
      {(report_title || period?.label) && (
        <div className="mini-header">
          {report_title && <h4 className="mini-title">{report_title}</h4>}
          {period?.label && <div className="mini-chip">{period.label}</div>}
        </div>
      )}

      {/* KPI grid */}
      {Array.isArray(kpis) && kpis.length > 0 && (
        <div className="mini-kpis">
          {kpis.map((k, i) => (
            <div key={i} className="mini-kpi">
              <div className="mini-kpi-label">{k.label}</div>
              <div className="mini-kpi-value">
                {formatValue(k.value)}
                {typeof k.delta_pct_vs_prev === "number" && (
                  <span className={`mini-delta ${k.delta_pct_vs_prev >= 0 ? "up" : "down"}`}>
                    {k.delta_pct_vs_prev >= 0 ? "▲" : "▼"} {Math.abs(k.delta_pct_vs_prev)}%
                  </span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Cards (Top lists, breakdowns) */}
      {Array.isArray(cards) && cards.length > 0 && (
        <div className="mini-cards">
          {cards.map((c, idx) => (
            <div key={idx} className="mini-card">
              <div className="mini-card-title">{c.title}</div>
              {Array.isArray(c.items) && c.items.length > 0 && (
                <div className="mini-table">
                  <div className="mini-row mini-row-head">
                    {Object.keys(c.items[0]).slice(0, 3).map((h) => (
                      <div key={h} className="mini-cell">{toLabel(h)}</div>
                    ))}
                  </div>
                  {c.items.slice(0, 6).map((row, rIdx) => (
                    <div key={rIdx} className="mini-row">
                      {Object.keys(c.items[0]).slice(0, 3).map((h) => (
                        <div key={h} className="mini-cell">{formatValue(row[h])}</div>
                      ))}
                    </div>
                  ))}
                </div>
              )}
              {Array.isArray(c.data) && c.data.length > 0 && (
                <div className="mini-table">
                  <div className="mini-row mini-row-head">
                    {Object.keys(c.data[0]).slice(0, 3).map((h) => (
                      <div key={h} className="mini-cell">{toLabel(h)}</div>
                    ))}
                  </div>
                  {c.data.slice(0, 6).map((row, rIdx) => (
                    <div key={rIdx} className="mini-row">
                      {Object.keys(c.data[0]).slice(0, 3).map((h) => (
                        <div key={h} className="mini-cell">{formatValue(row[h])}</div>
                      ))}
                    </div>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Simple chart preview (textual) */}
      {Array.isArray(charts) && charts.length > 0 && (
        <div className="mini-charts">
          {charts.slice(0, 1).map((ch, i) => (
            <div key={i} className="mini-chart">
              <div className="mini-card-title">{ch.title || "Chart"}</div>
              <div className="mini-chart-hint">
                {ch.type?.toUpperCase()} preview • {chartSummary(ch)}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function toLabel(key) {
  return key.replace(/_/g, " ").replace(/\b\w/g, (m) => m.toUpperCase());
}
function formatValue(v) {
  if (typeof v === "number") return Intl.NumberFormat().format(v);
  return String(v ?? "");
}
function chartSummary(ch) {
  if (Array.isArray(ch.data)) return `${ch.data.length} points`;
  if (Array.isArray(ch.series)) return `${ch.series.length} series`;
  return "data available";
}


