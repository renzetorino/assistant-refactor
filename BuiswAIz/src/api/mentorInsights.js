// src/api/mentorInsights.js
import { supabase } from '../supabase';

const API_BASE = import.meta.env.VITE_API_ASSISTANT_URL;
if (!API_BASE) {
  throw new Error('VITE_API_ASSISTANT_URL environment variable is not configured');
}

/**
 * Fetches the latest cached mentor insights for the current business.
 * Returns cached results if available and fresh (within 30 minutes).
 * 
 * @returns {Promise<Object>} Response with insights array, cache info, and metadata
 * @throws {Error} If the API request fails
 */
export async function fetchLatestInsights() {
  try {
    // Get current user's JWT token
    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(`${API_BASE}/api/mentor-insights/latest`, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.detail || `Failed to fetch insights: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('fetchLatestInsights error:', error);
    throw error;
  }
}

/**
 * Forces a fresh scan of business data and regenerates all insights.
 * Bypasses cache and runs detectors against live data.
 * Use this when you want to see immediately updated insights after making changes.
 * 
 * Rate limited to 10 requests per minute per business.
 * 
 * @returns {Promise<Object>} Response with freshly generated insights
 * @throws {Error} If the API request fails or rate limit is exceeded
 */
export async function refreshInsights() {
  try {
    // Get current user's JWT token
    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(`${API_BASE}/api/mentor-insights/refresh`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (response.status === 429) {
      throw new Error('Rate limit exceeded. Please wait a moment before refreshing again.');
    }

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.detail || `Failed to refresh insights: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('refreshInsights error:', error);
    throw error;
  }
}

/**
 * Helper function to get insights for a specific category.
 * 
 * @param {string} category - One of: 'inventory', 'finance', 'sales'
 * @returns {Promise<Object|null>} Single insight for the category or null if not found
 */
export async function fetchInsightByCategory(category) {
  try {
    const data = await fetchLatestInsights();
    const insight = data.insights?.find(i => i.category === category);
    return insight || null;
  } catch (error) {
    console.error(`fetchInsightByCategory(${category}) error:`, error);
    throw error;
  }
}

/**
 * Helper function to get the highest priority insight across all categories.
 * Priority: critical inventory issues > budget warnings > sales insights
 * 
 * @returns {Promise<Object|null>} Top priority insight or null if none found
 */
export async function fetchTopPriorityInsight() {
  try {
    const data = await fetchLatestInsights();
    const insights = data.insights || [];
    
    if (insights.length === 0) return null;

    // Priority order: inventory (stockout risks), finance (budget), sales
    const priorityOrder = ['inventory', 'finance', 'sales'];
    
    for (const category of priorityOrder) {
      const insight = insights.find(i => i.category === category);
      if (insight) return insight;
    }

    // Fallback to first available insight
    return insights[0];
  } catch (error) {
    console.error('fetchTopPriorityInsight error:', error);
    throw error;
  }
}

/**
 * Fetches Morning Briefing insight - top 3 business priorities for the day.
 * 
 * @returns {Promise<Object>} Response with insightType, content, generatedAt
 * @throws {Error} If the API request fails
 */
export async function fetchMorningBriefing() {
  try {
    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(`${API_BASE}/api/mentor-insights/morning-briefing`, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.detail || `Failed to fetch morning briefing: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('fetchMorningBriefing error:', error);
    throw error;
  }
}

/**
 * Fetches Inventory Risk insight for a specific product.
 * 
 * @param {number} productId - The product ID to analyze
 * @returns {Promise<Object>} Response with insightType, content, generatedAt, metadata
 * @throws {Error} If the API request fails
 */
export async function fetchInventoryRisk(productId) {
  try {
    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(`${API_BASE}/api/mentor-insights/inventory-risk/${productId}`, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.detail || `Failed to fetch inventory risk: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('fetchInventoryRisk error:', error);
    throw error;
  }
}

/**
 * Fetches Profit Teaching insight - explains revenue vs profit margins.
 * 
 * @param {number} periodDays - Analysis period in days (default: 90)
 * @returns {Promise<Object>} Response with insightType, content, generatedAt, metadata
 * @throws {Error} If the API request fails
 */
export async function fetchProfitTeaching(periodDays = 90) {
  try {
    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(`${API_BASE}/api/mentor-insights/profit-teaching?periodDays=${periodDays}`, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.detail || `Failed to fetch profit teaching: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('fetchProfitTeaching error:', error);
    throw error;
  }
}

/**
 * Fetches Expense Forecast insight - analyzes spending trends and seasonality.
 * 
 * @param {string|null} targetMonth - Target month (ISO format: YYYY-MM-DD or YYYY-MM). Defaults to current month.
 * @returns {Promise<Object>} Response with insightType, content, generatedAt, metadata
 * @throws {Error} If the API request fails
 */
export async function fetchExpenseForecast(targetMonth = null) {
  try {
    const { data: { session } } = await supabase.auth.getSession();
    if (!session?.access_token) {
      throw new Error('Not authenticated');
    }

    const url = targetMonth 
      ? `${API_BASE}/api/mentor-insights/expense-forecast?targetMonth=${encodeURIComponent(targetMonth)}`
      : `${API_BASE}/api/mentor-insights/expense-forecast`;

    const response = await fetch(url, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${session.access_token}`,
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.detail || `Failed to fetch expense forecast: ${response.status}`);
    }

    const data = await response.json();
    return data;
  } catch (error) {
    console.error('fetchExpenseForecast error:', error);
    throw error;
  }
}
