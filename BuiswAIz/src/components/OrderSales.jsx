import React, { useState, useEffect, useCallback, useRef } from 'react';

const OrderSales = ({ orderData, onInvoiceSelect, businessName }) => {
  const [filteredData, setFilteredData] = useState([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [sortOption, setSortOption] = useState('orderid-desc');
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
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

  const updateFilteredData = useCallback((data, search, sortOption) => {
    const filtered = data.filter(item => {
      const productName = item.products?.productname || '';
      const orderCode = item.orders?.ordercode || '';
      const matchesSearch =
        productName.toLowerCase().includes(search) ||
        orderCode.toLowerCase().includes(search) ||
        String(item.orderid).toLowerCase().includes(search);

      return matchesSearch;
    });

    const sortedData = sortData(filtered, sortOption);
    setFilteredData(sortedData);
  }, [sortData]);

  useEffect(() => {
    updateFilteredData(orderData, searchTerm, sortOption);
  }, [orderData, searchTerm, sortOption, updateFilteredData]);

  const handleSearch = (e) => {
    const value = e.target.value.toLowerCase();
    setSearchTerm(value);
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
    
    if (item.orders?.orderstatus) {
      status = item.orders.orderstatus;
    } else if (item.orderstatus) {
      status = item.orderstatus;
    } else if (item.orderItems && item.orderItems.length > 0 && item.orderItems[0]?.orders?.orderstatus) {
      status = item.orderItems[0].orders.orderstatus;
    }

    const normalizedStatus = status.toUpperCase();
    if (normalizedStatus === 'COMPLETE' || normalizedStatus === 'INCOMPLETE') {
      return normalizedStatus;
    }
    
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

  const getOrderCode = (item) => {
    return item.orders?.ordercode || `ORDER-${item.orderid}`;
  };

  const exportToCSV = () => {
    const businessHeader = businessName ? `Business: ${businessName}\n` : '';
    const exportDate = `Export Date: ${new Date().toLocaleString()}\n\n`;
    
    const headers = ['Product Name', 'Receipt Number', 'Status', 'Quantity', 'Price', 'Total Amount', 'Date'];
    
    const rows = filteredData.map(item => {
      const orderDate = item.orders?.orderdate || item.createdat;
      const orderCode = getOrderCode(item);
      return [
        item.products?.productname || 'N/A',
        orderCode,
        getOrderStatus(item),
        item.quantity,
        item.unitprice,
        item.subtotal,
        new Date(orderDate).toLocaleString()
      ];
    });
    
    const totalOrders = new Set(filteredData.map(item => item.orderid)).size;
    const totalRevenue = filteredData.reduce((sum, item) => sum + (item.subtotal || 0), 0);
    const totalItems = filteredData.reduce((sum, item) => sum + (item.quantity || 0), 0);
    
    const summarySection = `\n\nSummary:\nTotal Orders: ${totalOrders}\nTotal Items Sold: ${totalItems}\nTotal Revenue: ₱${totalRevenue.toLocaleString()}\n`;
    
    const csvContent = 
      businessHeader +
      exportDate +
      headers.join(',') + '\n' +
      rows.map(row => row.map(cell => `"${cell}"`).join(',')).join('\n') +
      summarySection;
    
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    
    const filename = businessName 
      ? `${businessName.replace(/\s+/g, '_')}_sales_orders_${new Date().toISOString().split('T')[0]}.csv`
      : `sales_orders_${new Date().toISOString().split('T')[0]}.csv`;
    
    link.setAttribute('href', url);
    link.setAttribute('download', filename);
    link.style.visibility = 'hidden';
    
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  return (
    <div className="sales-table-wrapper">
      <div className="table-header">
        <div className="panel-header-with-help">
          <div className="header-left-dash">
            <h3>Sales Orders</h3>
            <div className="help-wrapper-dash">
              <button 
                className="help-button-dash"
                onClick={() => setShowHelp(!showHelp)}
                aria-label="Help"
              >
                ?
              </button>
              {showHelp && (
                <div className="help-box-dash">
                  <div className="help-arrow-dash"></div>
                  
                  <div className="help-content-dash">
                    <p>GEN TIPS</p>
                  </div>
                  
                  <div className="help-separator-dash"></div>
                  
                  <div className="help-content-dash">
                    <p>AI TIPS</p>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
        
        <button 
          className="export-csv-btn"
          onClick={exportToCSV}
          title={`Export ${filteredData.length} order${filteredData.length !== 1 ? 's' : ''} to CSV`}
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
          placeholder="Search by product name or receipt number..."
          value={searchTerm}
          onChange={handleSearch}
        />
      </div>
      <div className="table-scroll-box">
        {filteredData.length === 0 ? (
          <div className="no-orders-message">
            <p>No sales orders found{searchTerm ? ' matching your search' : ' for this business'}.</p>
            {searchTerm && <small>Try adjusting your search terms</small>}
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                <th>Product Name</th>
                <th>Order Code</th>
                <th>Status</th>
                <th>Quantity</th>
                <th>Price</th>
                <th>Total Amount</th>
                <th>Ordered Date</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredData.map((item, index) => (
                <tr key={index}>
                  <td>{item.products?.productname || 'N/A'}</td>
                  <td style={{ fontFamily: 'monospace', fontSize: '13px' }}>
                    {getOrderCode(item)}
                  </td>
                  <td>{getStatusBadge(item)}</td>
                  <td>{item.quantity}</td>
                  <td>₱{item.unitprice.toLocaleString()}</td>
                  <td>₱{item.subtotal.toLocaleString()}</td>
                  <td>{new Date(item.orders?.orderdate || item.createdat).toLocaleDateString()}</td>
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
        )}
      </div>
    </div>
  );
};

export default OrderSales;