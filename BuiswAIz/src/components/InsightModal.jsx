import React from 'react';
import './InsightModal.css';

/**
 * Reusable modal for displaying AI insights.
 * Supports loading states, error handling, and formatted content.
 * 
 * @param {Object} props
 * @param {boolean} props.isOpen - Whether the modal is visible
 * @param {function} props.onClose - Callback to close the modal
 * @param {string} props.title - Modal title
 * @param {string|null} props.content - Insight content (supports markdown-style formatting)
 * @param {boolean} props.loading - Whether data is loading
 * @param {string|null} props.error - Error message if request failed
 * @param {string} props.insightType - Type of insight for custom styling
 */
const InsightModal = ({ 
  isOpen, 
  onClose, 
  title, 
  content, 
  loading = false, 
  error = null,
  insightType = 'generic'
}) => {
  if (!isOpen) return null;

  // Handle backdrop click
  const handleBackdropClick = (e) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  // Format content with enhanced markdown-like parsing
  const formatContent = (text) => {
    if (!text) return null;

    // Split by newlines and format
    const lines = text.split('\n').map((line, idx) => {
      // Section headers (lines ending with :)
      if (line.trim().endsWith(':') && line.trim().length > 3) {
        return <h3 key={idx} className="insight-section-header">{line.replace(/:$/, '')}</h3>;
      }
      // Numbered lists
      if (/^\d+\./.test(line)) {
        return <li key={idx} className="insight-list-item">{line.replace(/^\d+\.\s*/, '')}</li>;
      }
      // Bullet points
      if (/^[•\-\*]\s/.test(line)) {
        return <li key={idx} className="insight-bullet-item">{line.replace(/^[•\-\*]\s/, '')}</li>;
      }
      // Bold text (**text**)
      line = line.replace(/\*\*(.*?)\*\*/g, '<strong class="insight-emphasis">$1</strong>');
      // Italic text (*text*)
      line = line.replace(/\*(.*?)\*/g, '<em class="insight-highlight">$1</em>');
      // Empty lines as spacing
      if (line.trim() === '') {
        return <div key={idx} className="insight-spacer"></div>;
      }
      return <p key={idx} className="insight-paragraph" dangerouslySetInnerHTML={{ __html: line }} />;
    });

    return <div className="insight-content-formatted">{lines}</div>;
  };

  return (
    <div className="insight-modal-backdrop" onClick={handleBackdropClick}>
      <div className={`insight-modal insight-modal-${insightType}`}>
        <div className="insight-modal-header">
          <h2>{title}</h2>
          <button 
            className="insight-modal-close" 
            onClick={onClose}
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        <div className="insight-modal-body">
          {loading && (
            <div className="insight-loading">
              <div className="insight-spinner"></div>
              <p>Analyzing your business data...</p>
            </div>
          )}

          {error && (
            <div className="insight-error">
              <p className="insight-error-icon">⚠️</p>
              <p className="insight-error-message">{error}</p>
              <button 
                className="insight-retry-button" 
                onClick={() => window.location.reload()}
              >
                Retry
              </button>
            </div>
          )}

          {!loading && !error && content && (
            <div className="insight-content">
              {formatContent(content)}
            </div>
          )}

          {!loading && !error && !content && (
            <div className="insight-empty">
              <p>No insights available at this time.</p>
            </div>
          )}
        </div>

        <div className="insight-modal-footer">
          <button className="insight-close-button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
};

export default InsightModal;
