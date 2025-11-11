/**
 * Production-grade suggested prompts for AI Assistant
 * 
 * These are DISPLAY-ONLY suggestions to guide users on what questions they can ask.
 * Focus on basic queries that work well with NLQ/LLMSqlGenerator.
 * Avoid complex open-ended questions like "Why is my sales going down?"
 * 
 * Each prompt object contains:
 * - id: Unique identifier
 * - text: The suggested question to display
 * - category: Grouping for visual organization
 */

const prompts = [
  // ===== REVENUE QUERIES =====
  {
    id: 1,
    text: "What is our revenue for today?",
    category: "revenue"
  },
  {
    id: 2,
    text: "What was our total revenue last month?",
    category: "revenue"
  },
  {
    id: 3,
    text: "Show me revenue for September 2025",
    category: "revenue"
  },
  
  // ===== SALES QUERIES =====
  {
    id: 4,
    text: "How many orders did we receive today?",
    category: "sales"
  },
  {
    id: 5,
    text: "How many units were sold this week?",
    category: "sales"
  },
  {
    id: 6,
    text: "What is the total number of orders this month?",
    category: "sales"
  },
  
  // ===== EXPENSE QUERIES =====
  {
    id: 7,
    text: "How much did I spend on expenses today?",
    category: "expenses"
  },
  {
    id: 8,
    text: "What were my total expenses last week?",
    category: "expenses"
  },
  {
    id: 9,
    text: "Show me expenses for October 2025",
    category: "expenses"
  },
  
  // ===== INVENTORY QUERIES =====
  {
    id: 10,
    text: "How many products are currently in stock?",
    category: "inventory"
  }
];

export default prompts;
