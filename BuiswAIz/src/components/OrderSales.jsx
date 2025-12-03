import React, { useState, useEffect, useCallback, useRef } from 'react';

const OrderSales = ({ orderData, onInvoiceSelect }) => {
  const [filteredData, setFilteredData] = useState([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [filterTime, setFilterTime] = useState('all');
  const [sortOption, setSortOption] = useState('orderid-desc');
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const dropdownRef = useRef(null);

  const sortOptions = [
    { value: 'orderid-desc', label: 'Order ID (Descending)' },
    { value: 'orderid-asc', label: 'Order ID (Ascending)' },
    { value: 'product-az', label: 'Product Name (A-Z)' },
    { value: 'product-za', label: 'Product Name (Z-A)' },
    { value: 'amount-desc', label: 'Total Amount (Descending)'},
    { value: 'amount-asc', label: 'Total Amount (Ascending)' },
    { value: 'date-desc', label: 'Date (Newest)' },
    { value: 'date-asc', label: 'Date (Oldest)' }
  ];

  useEffect(() => {
    const handleClickOutside = (event) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
        setIsDropdownOpen(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const sortData = useCallback((data, sortOption) => {
    const sortedData = [...data];
    
    switch (sortOption) {
      case 'orderid-asc':
        return sortedData.sort((a, b) => a.orderid - b.orderid);
      case 'orderid-desc':
        return sortedData.sort((a, b) => b.orderid - a.orderid);
      case 'product-az':
        return sortedData.sort((a, b) => {
          const nameA = (a.products?.productname || '').toLowerCase();
          const nameB = (b.products?.productname || '').toLowerCase();
          return nameA.localeCompare(nameB);
        });
      case 'product-za':
        return sortedData.sort((a, b) => {
          const nameA = (a.products?.productname || '').toLowerCase();
          const nameB = (b.products?.productname || '').toLowerCase();
          return nameB.localeCompare(nameA);
        });
      case 'amount-asc':
        return sortedData.sort((a, b) => a.subtotal - b.subtotal);
      case 'amount-desc':
        return sortedData.sort((a, b) => b.subtotal - a.subtotal);
      case 'date-asc':
        return sortedData.sort((a, b) => {
          const dateA = new Date(a.orders?.orderdate || a.createdat);
          const dateB = new Date(b.orders?.orderdate || b.createdat);
          return dateA - dateB;
        });
      case 'date-desc':
        return sortedData.sort((a, b) => {
          const dateA = new Date(a.orders?.orderdate || a.createdat);
          const dateB = new Date(b.orders?.orderdate || b.createdat);
          return dateB - dateA;
        });
      default:
        return sortedData;
    }
  }, []);

  const updateFilteredData = useCallback((data, timeFilter, search, sortOption) => {
    const now = new Date();
    
    // Helper function to check if two dates are on the same day
    const isSameDay = (date1, date2) => {
      return date1.getFullYear() === date2.getFullYear() &&
             date1.getMonth() === date2.getMonth() &&
             date1.getDate() === date2.getDate();
    };

    const filtered = data.filter(item => {
      // FIXED: Use orders.orderdate first, fallback to createdat
      // This matches the Bestseller component logic
      let date;
      const orderDate = item.orders?.orderdate || item.createdat;
      
      if (orderDate) {
        date = new Date(orderDate);
        // Check if date is invalid
        if (isNaN(date.getTime())) {
          console.warn('Invalid date format for item:', item);
          return false;
        }
      } else {
        console.warn('No date field for item:', item);
        return false;
      }

      const productName = item.products?.productname || '';
      const matchesSearch =
        productName.toLowerCase().includes(search) ||
        String(item.orderid).toLowerCase().includes(search);

      if (!matchesSearch) return false;

      switch (timeFilter) {
        case 'today':
          return isSameDay(date, now);
        case 'week1':
          return date.getDate() <= 7 && 
                 date.getMonth() === now.getMonth() && 
                 date.getFullYear() === now.getFullYear();
        case 'week2':
          return date.getDate() > 7 && 
                 date.getDate() <= 14 && 
                 date.getMonth() === now.getMonth() && 
                 date.getFullYear() === now.getFullYear();
        case 'week3':
          return date.getDate() > 14 && 
                 date.getDate() <= 21 && 
                 date.getMonth() === now.getMonth() && 
                 date.getFullYear() === now.getFullYear();
        case 'month':
          return date.getMonth() === now.getMonth() && 
                 date.getFullYear() === now.getFullYear();
        default:
          return true;
      }
    });

    const sortedData = sortData(filtered, sortOption);
    setFilteredData(sortedData);
  }, [sortData]);

  useEffect(() => {
    updateFilteredData(orderData, filterTime, searchTerm, sortOption);
  }, [orderData, filterTime, searchTerm, sortOption, updateFilteredData]);

  const handleSearch = (e) => {
    const value = e.target.value.toLowerCase();
    setSearchTerm(value);
  };

  const handleTimeFilter = (e) => {
    const value = e.target.value;
    setFilterTime(value);
  };

  const handleSortSelect = (value) => {
    setSortOption(value);
    setIsDropdownOpen(false);
  };

  const getCurrentSortOption = () => {
    return sortOptions.find(option => option.value === sortOption);
  };

  const getOrderStatus = (item) => {
    let status = '';
    
    // Check multiple possible paths for order status
    if (item.orders?.orderstatus) {
      status = item.orders.orderstatus;
    } else if (item.orderstatus) {
      status = item.orderstatus;
    } else if (item.orderItems && item.orderItems.length > 0 && item.orderItems[0]?.orders?.orderstatus) {
      status = item.orderItems[0].orders.orderstatus;
    }

    // Normalize to uppercase and only return valid statuses
    const normalizedStatus = status.toUpperCase();
    if (normalizedStatus === 'COMPLETE' || normalizedStatus === 'INCOMPLETE') {
      return normalizedStatus;
    }
    
    // Default to INCOMPLETE if status is unknown or invalid
    return 'INCOMPLETE';
  };

  const getStatusBadge = (item) => {
    const status = getOrderStatus(item);
    let statusClass = '';
    let displayText = '';

    switch (status) {
      case 'COMPLETE':
        statusClass = 'status-completed';
        displayText = 'COMPLETE';
        break;
      case 'INCOMPLETE':
        statusClass = 'status-incomplete';
        displayText = 'INCOMPLETE';
        break;
      default:
        statusClass = 'status-incomplete';
        displayText = 'INCOMPLETE';
    }

    return (
      <span className={`status-badge ${statusClass}`}>
        {displayText}
      </span>
    );
  };

  const exportToCSV = () => {
    // Create sheet headers
    const headers = ['Product Name', 'Order Code', 'Status', 'Quantity', 'Price', 'Total Amount', 'Date'];
    
    // Create sheet rows from filtered data
    const rows = filteredData.map(item => {
      const orderDate = item.orders?.orderdate || item.createdat;
      return [
        item.products?.productname || 'N/A',
        item.orderid,
        getOrderStatus(item),
        item.quantity,
        item.unitprice,
        item.subtotal,
        new Date(orderDate).toLocaleString()
      ];
    });
    
    // Combine headers and rows
    const csvContent = [
      headers.join(','),
      ...rows.map(row => row.map(cell => `"${cell}"`).join(','))
    ].join('\n');
    
    // Create blob and download
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    
    link.setAttribute('href', url);
    link.setAttribute('download', `sales_orders_${new Date().toISOString().split('T')[0]}.csv`);
    link.style.visibility = 'hidden';
    
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  return (
    <div className="sales-table-wrapper">
      <div className="table-header">
        <h3>Sales Orders</h3>
        
        <button 
          className="export-csv-btn"
          onClick={exportToCSV}
        >
        Export to CSV
        </button>
        
        <div className="custom-dropdown-wrapper" ref={dropdownRef}>
          <div 
            className="custom-dropdown-button"
            onClick={() => setIsDropdownOpen(!isDropdownOpen)}
          >
            <div className="dropdown-button-content">
              <span className="dropdown-text">{getCurrentSortOption()?.label}</span>
              <span className={`dropdown-arrow ${isDropdownOpen ? 'open' : ''}`}>▼</span>
            </div>
          </div>
          
          {isDropdownOpen && (
            <div className="custom-dropdown-list">
              {sortOptions.map((option) => (
                <div
                  key={option.value}
                  className={`dropdown-item ${sortOption === option.value ? 'selected' : ''}`}
                  onClick={() => handleSortSelect(option.value)}
                >
                  <span className="item-text">{option.label}</span>
                </div>
              ))}
            </div>
          )}
        </div>

        <input
          type="text"
          className="search-input"
          placeholder="Search by product name or order code..."
          value={searchTerm}
          onChange={handleSearch}
        />
      </div>
      <div className="table-scroll-box">
        <table>
          <thead>
            <tr>
              <th>Product Name</th>
              <th>Order Code</th>
              <th>Status</th>
              <th>Quantity</th>
              <th>Price</th>
              <th>Total Amount</th>
              <th className="table-filter-header">
                <select 
                  className="table-filter" 
                  value={filterTime} 
                  onChange={handleTimeFilter}
                >
                  <option value="all">All Time</option>
                  <option value="today">Today</option>
                  <option value="week1">First Week</option>
                  <option value="week2">Second Week</option>
                  <option value="week3">Third Week</option>
                  <option value="month">This Month</option>
                </select>
              </th>
            </tr>
          </thead>
          <tbody>
            {filteredData.map((item, index) => (
              <tr key={index}>
                <td>{item.products?.productname || 'N/A'}</td>
                <td>{item.orderid}</td>
                <td>{getStatusBadge(item)}</td>
                <td>{item.quantity}</td>
                <td>₱{item.unitprice.toLocaleString()}</td>
                <td>₱{item.subtotal.toLocaleString()}</td>
                <td className="table-action">
                  <button 
                    className="invoice-btn"
                    onClick={() => onInvoiceSelect(item)}
                  >
                    View Invoice
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default OrderSales;