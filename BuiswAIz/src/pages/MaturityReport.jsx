/*
============================================================================
COMMENTED OUT: Business Maturity Report Page Component
============================================================================
REASON: The Business Maturity feature did not meet team and advisor standards.

KEY ISSUES IDENTIFIED:
- Backend uses placeholder logic (hardcoded values instead of real ActivityLog data)
- Data pollution: Writes to ai_insights table with category="maturity"
- Unreliable results for small datasets (default score of 50)
- Backend endpoints disabled in MaturityController.cs

REFERENCE: See ai-insights-flaws-analysis.md Flaw #7, #8, #9

FUTURE UPGRADE PLAN:
- Implement dedicated maturity_reports table (separate from ai_insights)
- Integrate real ActivityLog data for accurate Agility scoring
- Calculate Trust score from forecast adherence (not inventory updates)
- Add caching layer to prevent redundant calculations
- Redesign UI with better data visualization and actionable recommendations

DATE COMMENTED: January 15, 2026
============================================================================
*/
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { fetchMaturityReport, getMaturityLevelInfo, formatScore, getMonthName } from '../api/maturity';
import './MaturityReport.css';

/*
// Helper Components
const ScoreCard = ({ title, score, metric, metricLabel, metricUnit, icon, color }) => {
  return (
    <div className="score-card">
      <div className="score-card-header">
        <span className="score-icon" style={{ backgroundColor: `${color}20`, color }}>{icon}</span>
        <h3>{title}</h3>
      </div>
      <div className="score-value">{formatScore(score)}</div>
      <div className="score-meter">
        <div 
          className="score-meter-fill" 
          style={{ width: `${score}%`, backgroundColor: color }}
        ></div>
      </div>
      {metric !== null && metric !== undefined && (
        <div className="score-metric">
          <span className="metric-value-inline">{formatScore(metric)}</span> {metricUnit}
          <div className="metric-label-inline">{metricLabel}</div>
        </div>
      )}
    </div>
  );
};

const TrendsChart = ({ trends }) => {
  const maxScore = 100;
  const chartHeight = 200;
  const chartWidth = 600;
  const padding = 40;

  if (trends.length === 0) return null;

  const points = trends.map((trend, index) => ({
    x: padding + (index * (chartWidth - padding * 2) / (trends.length - 1 || 1)),
    agility: chartHeight - padding - ((trend.agilityScore / maxScore) * (chartHeight - padding * 2)),
    discipline: chartHeight - padding - ((trend.disciplineScore / maxScore) * (chartHeight - padding * 2)),
    trust: chartHeight - padding - ((trend.trustScore / maxScore) * (chartHeight - padding * 2)),
    health: chartHeight - padding - ((trend.healthScore / maxScore) * (chartHeight - padding * 2)),
    label: `${getMonthName(trend.month)} ${trend.year % 100}`
  }));

  const createPath = (scoreKey) => {
    return points.map((p, i) => 
      `${i === 0 ? 'M' : 'L'} ${p.x} ${p[scoreKey]}`
    ).join(' ');
  };

  return (
    <div className="chart-container">
      <svg viewBox={`0 0 ${chartWidth} ${chartHeight}`} className="trends-svg">
        {/* Grid lines */}
        {[0, 25, 50, 75, 100].map(value => {
          const y = chartHeight - padding - ((value / maxScore) * (chartHeight - padding * 2));
          return (
            <g key={value}>
              <line
                x1={padding}
                y1={y}
                x2={chartWidth - padding}
                y2={y}
                stroke="#e9ecef"
                strokeWidth="1"
              />
              <text x={padding - 10} y={y + 5} className="chart-label" textAnchor="end">
                {value}
              </text>
            </g>
          );
        })}

        {/* Lines */}
        <path d={createPath('health')} fill="none" stroke="#8b5cf6" strokeWidth="3" className="trend-line" />
        <path d={createPath('agility')} fill="none" stroke="#3b82f6" strokeWidth="2" strokeDasharray="5,5" />
        <path d={createPath('discipline')} fill="none" stroke="#10b981" strokeWidth="2" strokeDasharray="5,5" />
        <path d={createPath('trust')} fill="none" stroke="#f59e0b" strokeWidth="2" strokeDasharray="5,5" />

        {/* Points */}
        {points.map((p, i) => (
          <g key={i}>
            <circle cx={p.x} cy={p.health} r="5" fill="#8b5cf6" className="trend-point" />
            <text x={p.x} y={chartHeight - 10} className="chart-label" textAnchor="middle">
              {p.label}
            </text>
          </g>
        ))}
      </svg>

      <div className="chart-legend">
        <div className="legend-item">
          <span className="legend-line" style={{ backgroundColor: '#8b5cf6' }}></span>
          Health Score
        </div>
        <div className="legend-item">
          <span className="legend-line dashed" style={{ backgroundColor: '#3b82f6' }}></span>
          Agility
        </div>
        <div className="legend-item">
          <span className="legend-line dashed" style={{ backgroundColor: '#10b981' }}></span>
          Discipline
        </div>
        <div className="legend-item">
          <span className="legend-line dashed" style={{ backgroundColor: '#f59e0b' }}></span>
          Trust
        </div>
      </div>
    </div>
  );
};

// Main Component
const MaturityReport = () => {
  const navigate = useNavigate();
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    loadMaturityReport();
  }, []);

  const loadMaturityReport = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await fetchMaturityReport();
      setReport(data);
    } catch (err) {
      console.error('Failed to load maturity report:', err);
      setError(err.message || 'Failed to load maturity report');
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return (
      <div className="dashboard-page">
        <header className="header-bar">
          <h1 className="header-title">Bake-keri</h1>
        </header>
        <div className="main-section">
          <aside className="sidebar">
            <div className="nav-section">
              <p className="nav-header">GENERAL</p>
              <ul>
                <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
                <li onClick={() => navigate("/inventory")}>Inventory</li>
                <li onClick={() => navigate("/TablePage")}>Sales</li>
                {/* <li onClick={() => navigate("/expenses")}>Expenses</li> */}
                <li onClick={() => navigate("/assistant")}>AI Assistant</li>
                <li className="active">Business Maturity</li>
              </ul>
              <p className="nav-header">RELATED</p>
              <ul>
                <li onClick={() => navigate("/supplier")}>Supplier</li>
                {/* <li onClick={() => navigate("/pos")}>Point of Sales</li> */}
                <li onClick={() => navigate("/online-orders")}>Online Orders</li>
                {/* <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li> */}
              </ul>
            </div>
          </aside>
          <div className="main-content">
            <div className="maturity-report-container">
              <div className="loading-state">
                <div className="spinner"></div>
                <p>Analyzing your business performance...</p>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="dashboard-page">
        <header className="header-bar">
          <h1 className="header-title">Bake-keri</h1>
        </header>
        <div className="main-section">
          <aside className="sidebar">
            <div className="nav-section">
              <p className="nav-header">GENERAL</p>
              <ul>
                <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
                <li onClick={() => navigate("/inventory")}>Inventory</li>
                <li onClick={() => navigate("/TablePage")}>Sales</li>
                {/* <li onClick={() => navigate("/expenses")}>Expenses</li> */}
                <li onClick={() => navigate("/assistant")}>AI Assistant</li>
                <li className="active">Business Maturity</li>
              </ul>
              <p className="nav-header">RELATED</p>
              <ul>
                <li onClick={() => navigate("/supplier")}>Supplier</li>
                <li onClick={() => navigate("/pos")}>Point of Sales</li>
                <li onClick={() => navigate("/online-orders")}>Online Orders</li>
                {/* <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li> */}
              </ul>
            </div>
          </aside>
          <div className="main-content">
            <div className="maturity-report-container">
              <div className="error-state">
                <h3>⚠️ Unable to Load Report</h3>
                <p>{error}</p>
                <button onClick={loadMaturityReport} className="retry-button">
                  Retry
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (!report) {
    return null;
  }

  const levelInfo = getMaturityLevelInfo(report.maturityLevel);
  const currentScores = report.currentScores;

  return (
    <div className="dashboard-page">
      <header className="header-bar">
        <h1 className="header-title">Bake-keri</h1>
      </header>

      <div className="main-section">
        <aside className="sidebar">
          <div className="nav-section">
            <p className="nav-header">GENERAL</p>
            <ul>
              <li onClick={() => navigate("/Dashboard")}>Dashboard</li>
              <li onClick={() => navigate("/inventory")}>Inventory</li>
              <li onClick={() => navigate("/TablePage")}>Sales</li>
              {/* <li onClick={() => navigate("/expenses")}>Expenses</li> */}
              <li onClick={() => navigate("/assistant")}>AI Assistant</li>
              <li className="active">Business Maturity</li>
            </ul>
            <p className="nav-header">RELATED</p>
            <ul>
              <li onClick={() => navigate("/supplier")}>Supplier</li>
              {/* <li onClick={() => navigate("/pos")}>Point of Sales</li> */}
              <li onClick={() => navigate("/online-orders")}>Online Orders</li>
              {/* <li onClick={() => navigate("/PlannedPaymentsPage")}>Planned Payment</li> */}
            </ul>
          </div>
        </aside>

        <div className="main-content">
          <div className="maturity-report-container">
            <header className="report-header">
              <h1>📊 My Growth Dashboard</h1>
              <p className="subtitle">Track your operational excellence journey</p>
            </header>

      {/* Health Score Gauge */}
      <section className="health-score-section">
        <div className="health-gauge-wrapper">
          <div className="health-gauge">
            <svg viewBox="0 0 200 120" className="gauge-svg">
              <defs>
                <linearGradient id="gaugeGradient" x1="0%" y1="0%" x2="100%" y2="0%">
                  <stop offset="0%" style={{ stopColor: '#ff6b6b' }} />
                  <stop offset="33%" style={{ stopColor: '#ffa94d' }} />
                  <stop offset="66%" style={{ stopColor: '#51cf66' }} />
                  <stop offset="100%" style={{ stopColor: '#339af0' }} />
                </linearGradient>
              </defs>
              
              {/* Background arc */}
              <path
                d="M 20 100 A 80 80 0 0 1 180 100"
                fill="none"
                stroke="#e9ecef"
                strokeWidth="20"
                strokeLinecap="round"
              />
              
              {/* Progress arc */}
              <path
                d="M 20 100 A 80 80 0 0 1 180 100"
                fill="none"
                stroke="url(#gaugeGradient)"
                strokeWidth="20"
                strokeLinecap="round"
                strokeDasharray={`${(currentScores.healthScore / 100) * 251} 251`}
                className="gauge-progress"
              />
            </svg>
            
            <div className="gauge-content">
              <div className="gauge-score">{formatScore(currentScores.healthScore)}</div>
              <div className="gauge-label">Health Score</div>
              <div className="gauge-level" style={{ color: levelInfo.color }}>
                {levelInfo.label}
              </div>
            </div>
          </div>
        </div>

        <div className="level-description">
          <h3>{levelInfo.label} Level</h3>
          <p>{levelInfo.description}</p>
        </div>
      </section>

      {/* Score Breakdown */}
      <section className="scores-grid">
        <ScoreCard
          title="Agility"
          score={currentScores.agilityScore}
          metric={currentScores.metrics.avgResponseTimeHours}
          metricLabel="Avg Response Time"
          metricUnit="hours"
          icon="⚡"
          color="#8b5cf6"
        />
        <ScoreCard
          title="Discipline"
          score={currentScores.disciplineScore}
          metric={currentScores.metrics.avgBookkeepingLagDays}
          metricLabel="Bookkeeping Lag"
          metricUnit="days"
          icon="📋"
          color="#3b82f6"
        />
        <ScoreCard
          title="Trust"
          score={currentScores.trustScore}
          metric={currentScores.metrics.forecastAdherencePercent}
          metricLabel="Forecast Adherence"
          metricUnit="%"
          icon="🎯"
          color="#10b981"
        />
      </section>

      {/* Monthly Trends Chart */}
      <section className="trends-section">
        <h2>📈 3-Month Trends</h2>
        <div className="trends-chart">
          {report.monthlyTrends.length > 0 ? (
            <TrendsChart trends={report.monthlyTrends} />
          ) : (
            <p className="no-data">Not enough data to show trends yet. Keep tracking!</p>
          )}
        </div>
      </section>

      {/* Metrics Details */}
      <section className="metrics-details">
        <h2>📊 Detailed Metrics</h2>
        <div className="metrics-table">
          <div className="metric-row">
            <span className="metric-label">Stockout Alerts Handled</span>
            <span className="metric-value">{currentScores.metrics.stockoutAlertCount}</span>
          </div>
          <div className="metric-row">
            <span className="metric-label">Expenses Recorded</span>
            <span className="metric-value">{currentScores.metrics.expenseCount}</span>
          </div>
          <div className="metric-row">
            <span className="metric-label">Forecast Comparisons</span>
            <span className="metric-value">{currentScores.metrics.forecastComparisonCount}</span>
          </div>
        </div>
      </section>
          </div>
        </div>
      </div>
    </div>
  );
};

export default MaturityReport;
*/
