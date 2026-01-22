/*
============================================================================
COMMENTED OUT: Business Maturity API Module
============================================================================
REASON: The Business Maturity feature did not meet team and advisor standards.

KEY ISSUES IDENTIFIED:
- Backend uses placeholder logic (hardcoded values instead of real data)
- Data pollution: Writes to ai_insights table with category="maturity"
- Unreliable results for small datasets (default score of 50)
- Backend endpoints disabled in MaturityController.cs

REFERENCE: See ai-insights-flaws-analysis.md Flaw #7, #8, #9

FUTURE UPGRADE PLAN:
- Implement dedicated maturity_reports table
- Integrate real ActivityLog data for accurate scoring
- Calculate Trust score from forecast adherence
- Add caching layer to prevent redundant calculations

DATE COMMENTED: January 15, 2026
============================================================================
*/
import { supabase } from '../supabase';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;
if (!API_BASE_URL) {
  throw new Error('VITE_API_BASE_URL environment variable is not configured');
}

/**
 * Fetches the business maturity report with health scores, trends, and AI coaching.
 * @returns {Promise<Object>} Maturity report data
 */
/*
export const fetchMaturityReport = async () => {
  try {
    const { data: { session } } = await supabase.auth.getSession();
    
    if (!session?.access_token) {
      throw new Error('No active session');
    }

    const response = await fetch(`${API_BASE_URL}/api/maturity/report`, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.error || `HTTP ${response.status}: ${response.statusText}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('[maturityAPI] Failed to fetch maturity report:', error);
    throw error;
  }
};

/**
 * Gets the maturity level label with color.
 * @param {string} level - Maturity level (Novice, Operator, Manager, Strategist)
 * @returns {Object} Level info with label and color
 */
export const getMaturityLevelInfo = (level) => {
  const levels = {
    'Novice': { label: 'Novice', color: '#ff6b6b', description: 'Building foundations' },
    'Operator': { label: 'Operator', color: '#ffa94d', description: 'Managing daily operations' },
    'Manager': { label: 'Manager', color: '#51cf66', description: 'Optimizing processes' },
    'Strategist': { label: 'Strategist', color: '#339af0', description: 'Leading with insight' }
  };

  return levels[level] || levels['Novice'];
};

/**
 * Formats a score value for display.
 * @param {number} score - Score value (0-100)
 * @returns {string} Formatted score
 */
export const formatScore = (score) => {
  return score ? score.toFixed(1) : '0.0';
};

/**
 * Gets month name from month number.
 * @param {number} month - Month number (1-12)
 * @returns {string} Month abbreviation
 */
export const getMonthName = (month) => {
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 
                  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return months[month - 1] || '';
};
*/
