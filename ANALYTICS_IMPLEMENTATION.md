# Analytics Implementation Summary

## Overview
This document describes the analytics tabs added to SellerPanel and AdminPanel, including all features, SQL queries (via UDP), and file structure.

## Files Modified/Created

### 1. Controllers

#### `Controllers/SellerController.cs`
- **Modified**: `GetAnalytics()` method
  - Added filter parameters: `startDate`, `endDate`, `categoryId`
  - Implemented abandoned basket analytics
  - Enhanced date range filtering for all analytics sections
  - Added category filtering support

**Key Features:**
- Stock Analytics: Products running out, out of stock, stock update timeline
- Order Analytics: Orders per day/week/month, average order amount, status distribution
- Top Selling Products: Revenue and quantity sold
- Reviews Analytics: Total reviews, average rating
- Basket Analytics: Abandoned baskets (baskets not converted to orders within 7 days)
- New/Inactive Products: Products created in last 90 days, inactive products

#### `Controllers/AdminController.cs`
- **Modified**: `GetAnalytics()` method
  - Added filter parameters: `startDate`, `endDate`, `categoryId`
  - Enhanced all analytics sections with date range filtering
  - Added category filtering support

**Key Features:**
- Store Performance: Sales by store, average order value, registration timeline
- Category Analytics: Products, orders, revenue per category
- Global Sales Dynamics: Orders per month, total turnover, cancelled orders percentage
- User Analytics: Total users, active users, user spending patterns
- Best/Worst Products: Top and bottom performers by revenue
- Review Analytics: Global average rating, reviews per product
- Price & Discount Analytics: Price ranges, average discounts

#### `Controllers/HomeController.cs`
- **Added**: `GetCategories()` method
  - Returns JSON list of all categories for filter dropdowns
  - Used by both Seller and Admin analytics filters

### 2. Views

#### `Views/Seller/Index.cshtml`
- **Modified**: Analytics tab section
  - Added filter controls (date range, category)
  - Enhanced `loadSellerAnalytics()` to support filters
  - Added basket analytics display section
  - Added category loading on page load
  - Set default date range (last 30 days)

**UI Components:**
- Date range pickers (start date, end date)
- Category dropdown filter
- "Apply Filters" button
- Basket analytics table with abandoned baskets

#### `Views/Admin/Index.cshtml`
- **Modified**: Analytics tab section
  - Added filter controls (date range, category)
  - Enhanced `loadAdminAnalytics()` to support filters
  - Added category loading on page load
  - Set default date range (last 12 months)

**UI Components:**
- Date range pickers (start date, end date)
- Category dropdown filter
- "Apply Filters" button

### 3. Models

#### `Models/SellerAnalyticsViewModel.cs`
- Already existed with all required properties
- Used for serializing analytics data to JSON

#### `Models/AdminAnalyticsViewModel.cs`
- Already existed with all required properties
- Used for serializing analytics data to JSON

## Data Access Pattern

The application uses **UDP communication** with a backend server, not direct SQL queries. All data is retrieved via `UdpClientHelper.SendUdpMessage()` commands:

### Seller Analytics Queries (via UDP):
- `getproducts` - Get all products
- `getallstores` - Get all stores
- `getusers` - Get all users
- `getuserorders|{userId}` - Get orders for a user
- `getproductreviewsall|{productId}` - Get all reviews for a product
- `getbasket|{userId}` - Get user's basket
- `getallcategories` - Get all categories

### Admin Analytics Queries (via UDP):
- Same as Seller, plus:
- Aggregated across all stores and users
- Global statistics

## Analytics Features

### Seller Analytics Tab

#### A) Stock Analytics
- **Products running out**: Quantity < threshold (default: 10)
- **Out of stock**: Quantity = 0
- **LastUpdate timeline**: Bar chart showing stock updates over time
- **DataGrid**: Table with product name, quantity, last update

#### B) Order Analytics
- **Orders per day/week/month**: Line charts
- **Average order amount**: Calculated statistic
- **Order status distribution**: Pie chart
- **DataGrid**: Order statistics table

#### C) Top Selling Products
- **DataGrid**: Product name, quantity sold, revenue
- **Bar chart**: Revenue visualization

#### D) Reviews Analytics
- **Statistics**: Total reviews, average rating
- **DataGrid**: Reviews per product

#### E) Basket Analytics
- **Abandoned baskets**: Baskets not converted to orders within 7 days
- **DataGrid**: User, product count, total amount, last activity
- **Total count**: Summary statistic

#### F) New/Inactive Products
- **New products**: Created in last 90 days
- **Inactive products**: No orders in last 90 days
- **DataGrid**: Product name, creation date, days since last order

### Admin Analytics Tab

#### A) Store Performance Analytics
- **Sales by store**: Total sales per store
- **Average order value**: Per store
- **Registration timeline**: Bar chart of store registrations
- **DataGrid**: Store performance table

#### B) Category Analytics
- **Products per category**: Count
- **Orders per category**: Count
- **Revenue per category**: Total
- **Doughnut chart**: Revenue distribution

#### C) Global Sales Dynamics
- **Orders per month**: Line chart
- **Total turnover**: Sum of all orders
- **Cancelled orders %**: Percentage calculation
- **DataGrid**: Monthly statistics

#### D) User Analytics
- **Total users**: Count
- **Active users**: Count
- **User spending**: Top 20 users by spending
- **DataGrid**: User statistics

#### E) Best/Worst Products
- **Best products**: Top 10 by revenue
- **Worst products**: Bottom 10 by revenue
- **DataGrid**: Product name, revenue, rating

#### F) Review Analytics
- **Global average rating**: Across all products
- **Reviews per product**: Top 20 products
- **DataGrid**: Review statistics

#### G) Price & Discount Analytics
- **Average price**: Across all products
- **Average discount**: Across all products
- **Price ranges**: Distribution by price ranges
- **Bar chart**: Products per price range

## Charts Used

All charts use **Chart.js** (loaded from CDN):
- **Bar charts**: Stock timeline, top products, price ranges, store registrations
- **Line charts**: Orders by day/month, sales by month
- **Pie charts**: Order status distribution
- **Doughnut charts**: Category revenue

## Filtering

### Date Range Filters
- **Seller**: Default last 30 days
- **Admin**: Default last 12 months
- Applied to:
  - Order analytics
  - Stock update timeline
  - Sales dynamics

### Category Filters
- Dropdown populated from `getallcategories` UDP command
- Filters products and related analytics
- "All categories" option available

## Integration Points

1. **Tab Navigation**: Analytics tabs are integrated into existing TabControl in both panels
2. **AJAX Loading**: Analytics load on-demand when tab is shown
3. **Filter Persistence**: Filters are applied on each load (not persisted across sessions)
4. **Error Handling**: Graceful degradation if server is unavailable

## Testing Checklist

- [x] Seller analytics tab loads correctly
- [x] Admin analytics tab loads correctly
- [x] Date range filters work
- [x] Category filters work
- [x] All charts render properly
- [x] DataGrids display data correctly
- [x] Basket analytics shows abandoned baskets
- [x] All statistics calculate correctly
- [x] No JavaScript errors in console
- [x] Responsive design works on different screen sizes

## Notes

- All analytics are calculated client-side from data retrieved via UDP
- Basket analytics requires checking all users' baskets and comparing with orders
- Date filtering is applied to order-based analytics
- Category filtering filters products first, then related analytics
- Charts are rendered using Chart.js library (v3.9.1)

