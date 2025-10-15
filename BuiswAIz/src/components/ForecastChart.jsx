// src/components/ForecastChart.jsx
import React, { useMemo } from "react";
import {
  LineChart,
  Line,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";

/**
 * Reusable chart component for forecast visualization
 */
export default function ForecastChart({ data }) {
  const chartData = useMemo(() => {
    if (!data?.series) return [];

    const history = data.series.history || [];
    const forecast = data.series.forecast || [];

    // Combine history and forecast
    const combined = [
      ...history.slice(-30).map((d) => ({
        date: d.date,
        actual: d.value,
        forecast: null,
        lower: null,
        upper: null,
        type: "historical",
      })),
      ...forecast.map((d) => ({
        date: d.date,
        actual: null,
        forecast: d.value,
        lower: d.lower,
        upper: d.upper,
        type: "forecast",
      })),
    ];

    return combined;
  }, [data]);

  const formatDate = (dateStr) => {
    try {
      const date = new Date(dateStr);
      return new Intl.DateTimeFormat("en-US", {
        month: "short",
        day: "numeric",
      }).format(date);
    } catch {
      return dateStr;
    }
  };

  const formatCurrency = (value) => {
    if (value == null) return "";
    return new Intl.NumberFormat("en-PH", {
      style: "currency",
      currency: "PHP",
      maximumFractionDigits: 0,
    }).format(value);
  };

  const CustomTooltip = ({ active, payload, label }) => {
    if (!active || !payload || payload.length === 0) return null;

    const data = payload[0].payload;

    return (
      <div
        style={{
          background: "white",
          border: "1px solid #ddd",
          borderRadius: 8,
          padding: 12,
          boxShadow: "0 2px 8px rgba(0,0,0,0.1)",
        }}
      >
        <div style={{ fontWeight: 600, marginBottom: 8 }}>{formatDate(label)}</div>
        {data.actual != null && (
          <div style={{ color: "#1976d2", fontSize: 14 }}>
            Actual: {formatCurrency(data.actual)}
          </div>
        )}
        {data.forecast != null && (
          <>
            <div style={{ color: "#2e7d32", fontSize: 14, fontWeight: 600 }}>
              Forecast: {formatCurrency(data.forecast)}
            </div>
            {data.lower != null && data.upper != null && (
              <div style={{ color: "#666", fontSize: 12, marginTop: 4 }}>
                Range: {formatCurrency(data.lower)} - {formatCurrency(data.upper)}
              </div>
            )}
          </>
        )}
      </div>
    );
  };

  if (chartData.length === 0) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#999" }}>
        No data available for chart
      </div>
    );
  }

  return (
    <ResponsiveContainer width="100%" height={350}>
      <AreaChart data={chartData} margin={{ top: 10, right: 30, left: 0, bottom: 0 }}>
        <defs>
          <linearGradient id="colorForecast" x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor="#2e7d32" stopOpacity={0.1} />
            <stop offset="95%" stopColor="#2e7d32" stopOpacity={0} />
          </linearGradient>
          <linearGradient id="colorActual" x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor="#1976d2" stopOpacity={0.2} />
            <stop offset="95%" stopColor="#1976d2" stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
        <XAxis
          dataKey="date"
          tickFormatter={formatDate}
          style={{ fontSize: 12 }}
          stroke="#666"
        />
        <YAxis
          tickFormatter={(value) => `₱${(value / 1000).toFixed(0)}k`}
          style={{ fontSize: 12 }}
          stroke="#666"
        />
        <Tooltip content={<CustomTooltip />} />
        <Legend
          wrapperStyle={{ paddingTop: 20 }}
          iconType="line"
          formatter={(value) => {
            const labels = {
              actual: "Historical",
              forecast: "Forecast",
              lower: "Lower Bound",
              upper: "Upper Bound",
            };
            return labels[value] || value;
          }}
        />
        
        {/* Confidence interval area */}
        <Area
          type="monotone"
          dataKey="upper"
          stroke="none"
          fill="#c8e6c9"
          fillOpacity={0.3}
          connectNulls
        />
        <Area
          type="monotone"
          dataKey="lower"
          stroke="none"
          fill="white"
          fillOpacity={1}
          connectNulls
        />
        
        {/* Actual line */}
        <Area
          type="monotone"
          dataKey="actual"
          stroke="#1976d2"
          strokeWidth={2}
          fill="url(#colorActual)"
          connectNulls
          dot={{ r: 3, fill: "#1976d2" }}
        />
        
        {/* Forecast line */}
        <Area
          type="monotone"
          dataKey="forecast"
          stroke="#2e7d32"
          strokeWidth={3}
          strokeDasharray="5 5"
          fill="url(#colorForecast)"
          connectNulls
          dot={{ r: 4, fill: "#2e7d32" }}
        />
      </AreaChart>
    </ResponsiveContainer>
  );
}
