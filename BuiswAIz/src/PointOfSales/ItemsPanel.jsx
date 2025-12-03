import { useState } from 'react';
import "../stylecss/PointOfSales/ItemsPanel.css";

const ItemsPanel = ({ 
  products, 
  searchTerm, 
  setSearchTerm, 
  selectedCategory, 
  setSelectedCategory, 
  categories, 
  onAddToCart 
}) => {
  const [sortBy, setSortBy] = useState('name-asc');

  const filteredProducts = products.filter(product => {
    const matchesSearch = product.displayName.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesCategory = selectedCategory === 'All' || product.categoryLabel === selectedCategory;
    return matchesSearch && matchesCategory;
  });

  const sortedProducts = [...filteredProducts].sort((a, b) => {
    switch (sortBy) {
      case 'name-asc':
        return a.productname.localeCompare(b.productname);
      case 'name-desc':
        return b.productname.localeCompare(a.productname);
      case 'price-asc':
        return a.price - b.price;
      case 'price-desc':
        return b.price - a.price;
      case 'stock-asc':
        return a.currentstock - b.currentstock;
      case 'stock-desc':
        return b.currentstock - a.currentstock;
      default:
        return 0;
    }
  });

  return (
    <div className="pos-products-section">
      <div className="pos-controls">
        <div className="search-sort-row">
          <input
            type="text"
            placeholder="Search products..."
            className="pos-search"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
          />
          <select 
            className="pos-sort"
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value)}
          >
            <option value="name-asc">Name (A-Z)</option>
            <option value="name-desc">Name (Z-A)</option>
            <option value="price-asc">Price (Low-High)</option>
            <option value="price-desc">Price (High-Low)</option>
            <option value="stock-asc">Stock (Low-High)</option>
            <option value="stock-desc">Stock (High-Low)</option>
          </select>
        </div>
        <div className="pos-categories">
          {categories.map(category => (
            <button
              key={category}
              className={`category-btn ${selectedCategory === category ? 'active' : ''}`}
              onClick={() => setSelectedCategory(category)}
            >
              {category}
            </button>
          ))}
        </div>
      </div>

      <div className="products-grid">
        {sortedProducts.length === 0 ? (
          <div className="empty-products">
            <p>No products available</p>
          </div>
        ) : (
          sortedProducts.map(product => (
            <div 
              key={product.productcategoryid} 
              className="product-card" 
              onClick={() => onAddToCart(product)}
            >
              {/* Full image as background */}
              <div className="product-image-wrapper">
                {product.image_url ? (
                  <img src={product.image_url} alt={product.displayName} className="product-full-image" />
                ) : (
                  <div className="product-no-image">
                    <p>No Image</p>
                  </div>
                )}
              </div>
              
              {/* White details box overlaying bottom */}
              <div className="product-overlay">
                <div className="product-info">
                  <h4>{product.productname}</h4>
                  {(product.color || product.agesize) && (
                    <p className="product-variant">
                      {[product.color, product.agesize].filter(Boolean).join(' • ')}
                    </p>
                  )}
                </div>
                <div className="product-footer">
                  <span className="product-price">₱{product.price.toFixed(2)}</span>
                  <span className="product-stock">Stock: {product.currentstock}</span>
                </div>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
};

export default ItemsPanel;