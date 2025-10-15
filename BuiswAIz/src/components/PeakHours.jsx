import React, { useMemo } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';

const PeakHours = ({ orderData }) => {
  // Process order data to get sales by 3-hour intervals
  const peakHoursData = useMemo(() => {
    if (!orderData || orderData.length === 0) return [];

    // Define 3-hour intervals
    const intervals = [
      { label: '12AM-3AM', start: 0, end: 3 },
      { label: '3AM-6AM', start: 3, end: 6 },
      { label: '6AM-9AM', start: 6, end: 9 },
      { label: '9AM-12PM', start: 9, end: 12 },
      { label: '12PM-3PM', start: 12, end: 15 },
      { label: '3PM-6PM', start: 15, end: 18 },
      { label: '6PM-9PM', start: 18, end: 21 },
      { label: '9PM-12AM', start: 21, end: 24 }
    ];

    // Initialize counts for each interval
    const intervalCounts = intervals.map(interval => ({
      timeRange: interval.label,
      sales: 0,
      revenue: 0
    }));

    // Process each order
    orderData.forEach(item => {
      const orderDate = new Date(item.createdat);
      const hour = orderDate.getHours();
      
      // Find which interval this hour belongs to
      const intervalIndex = intervals.findIndex(interval => 
        hour >= interval.start && hour < interval.end
      );
      
      if (intervalIndex !== -1) {
        intervalCounts[intervalIndex].sales += 1;
        intervalCounts[intervalIndex].revenue += (item.subtotal || 0);
      }
    });

    return intervalCounts;
  }, [orderData]);

  // Custom tooltip for the chart
  const CustomTooltip = ({ active, payload, label }) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      return (
        <div className="peak-hours-tooltip">
          <p className="tooltip-label">{label}</p>
          <p className="tooltip-sales">
            Sales: <span className="tooltip-value">{data.sales}</span>
          </p>
          <p className="tooltip-revenue">
            Revenue: <span className="tooltip-value">₱{data.revenue.toLocaleString()}</span>
          </p>
        </div>
      );
    }
    return null;
  };

  // Find peak hour for highlight
  const peakInterval = useMemo(() => {
    return peakHoursData.reduce((max, current) => 
      current.sales > max.sales ? current : max, 
      { sales: 0, timeRange: '' }
    );
  }, [peakHoursData]);

  return (
    <div className="peak-hours-wrapper">
      <div className="peak-hours-header">
        <h3>Peak Hours Sales</h3>
        {peakInterval.sales > 0 && (
          <div className="peak-indicator">
            <span className="peak-time">Peak: {peakInterval.timeRange}</span>
            <span className="peak-count">({peakInterval.sales} sales)</span>
          </div>
        )}
      </div>
      
      <div className="peak-hours-chart-container">
        {peakHoursData.length > 0 ? (
          <ResponsiveContainer width="100%" height="100%">
            <LineChart
              data={peakHoursData}
              margin={{
                top: 10,
                right: 10,
                left: 0,
                bottom: 20
              }}
            >
              <CartesianGrid strokeDasharray=" 3" stroke="#f0f0f0" />
              <XAxis 
                dataKey="timeRange" 
                tick={{ fontSize: 10, fill: '#666' }}
                angle={-20}
                textAnchor="end"
                height={10}
                interval={0}
              />
              <YAxis 
                tick={{ fontSize: 10, fill: '#666' }}
                label={{ value: 'Sales Count', angle: -90, position: 'insideLeft', style: { textAnchor: 'middle', fontSize: '10px', fill: '#666' } }}
              />
              <Tooltip content={<CustomTooltip />} />
              <Line 
                type="monotone" 
                dataKey="sales" 
                stroke="#04B4FC" 
                strokeWidth={2}
                dot={{ fill: '#04B4FC', strokeWidth: 2, r: 3 }}
                activeDot={{ r: 4, stroke: '#04B4FC', strokeWidth: 0, fill: '#ffffff' }}
              />
            </LineChart>
          </ResponsiveContainer>
        ) : (
          <div className="no-data-message">
            <p>No sales data available</p>
            <small>Sales data will appear here once orders are recorded</small>
          </div>
        )}
      </div>

      {peakHoursData.length > 0 && (
        <div className="peak-hours-summary">
          <div className="summary-stats">
            <div className="stat-item">
              <span className="stat-label">Total Sales:</span>
              <span className="stat-value">
                {peakHoursData.reduce((sum, item) => sum + item.sales, 0)}
              </span>
            </div>
            <div className="stat-item">
              <span className="stat-label">Busiest Period:</span>
              <span className="stat-value">{peakInterval.timeRange}</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default PeakHours;