import { useState } from 'react';
import '../stylecss/FaqSearch.css';

const FaqSearch = () => {
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);

  // Get API URL from environment or use localhost for development
  const API_URL = import.meta.env.VITE_API_URL || 'http://localhost:5000';

  const handleSearch = async (e) => {
    e.preventDefault();
    
    if (!query.trim()) {
      setError('Please enter a question');
      return;
    }

    setLoading(true);
    setError(null);
    setResult(null);

    try {
      const response = await fetch(`${API_URL}/api/faq/search`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          query: query.trim(),
          userId: localStorage.getItem('userId') || null,
        }),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const data = await response.json();
      setResult(data);
    } catch (err) {
      console.error('FAQ search error:', err);
      setError('Failed to search FAQ. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleClear = () => {
    setQuery('');
    setResult(null);
    setError(null);
  };

  return (
    <div className="faq-search-container">
      <div className="faq-search-header">
        <h2>💡 FAQ Assistant</h2>
        <p>Ask questions about forecasting, sales, inventory, and more</p>
      </div>

      <form onSubmit={handleSearch} className="faq-search-form">
        <div className="faq-input-group">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Ask a question... (e.g., How accurate are the forecasts?)"
            className="faq-search-input"
            disabled={loading}
          />
          <button
            type="submit"
            disabled={loading || !query.trim()}
            className="faq-search-button"
          >
            {loading ? (
              <>
                <span className="spinner"></span> Searching...
              </>
            ) : (
              <>🔍 Search</>
            )}
          </button>
          {(result || error) && (
            <button
              type="button"
              onClick={handleClear}
              className="faq-clear-button"
            >
              Clear
            </button>
          )}
        </div>
      </form>

      {error && (
        <div className="faq-error">
          <span className="error-icon">⚠️</span>
          <p>{error}</p>
        </div>
      )}

      {result && (
        <div className="faq-result">
          <div className="faq-result-header">
            <h3>Answer</h3>
            {result.confidence > 0 && (
              <span className="confidence-badge">
                Confidence: {(result.confidence * 100).toFixed(1)}%
              </span>
            )}
          </div>
          
          <div className="faq-answer">
            <p>{result.answer}</p>
          </div>

          {result.sources && result.sources.length > 0 && (
            <div className="faq-sources">
              <h4>📚 Sources</h4>
              <ul>
                {result.sources.map((source, index) => (
                  <li key={index} className="source-item">
                    <span className="source-content">{source.content}</span>
                    {source.pageNumber && (
                      <span className="source-page">Page {source.pageNumber}</span>
                    )}
                    {source.score > 0 && (
                      <span className="source-score">
                        {(source.score * 100).toFixed(0)}% match
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default FaqSearch;
