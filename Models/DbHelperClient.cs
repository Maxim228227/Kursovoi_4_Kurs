using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace Kursovoi.Models
{
    // Lightweight DB helper used on client side (web app) to perform direct DB queries when UDP server is not used.
    public class DbHelperClient
    {
        private readonly string _conn;
        public string LastError { get; private set; } = string.Empty;

        public DbHelperClient(IConfiguration cfg)
        {
            // try to read connection string from configuration, fall back to environment variable or empty
            _conn = cfg.GetConnectionString("DefaultConnection") ?? Environment.GetEnvironmentVariable("KURSOVOI_CONNECTION") ?? string.Empty;
        }

        private SqlConnection CreateConnectionWithOptionalTrust(string conn)
        {
            // return a SqlConnection using given conn
            return new SqlConnection(conn);
        }

        // New: return list of StoreIDs associated with user (supports UserStores mapping table or fallback to Users.StoreID)
        public List<int> GetStoreIdsForUser(string login)
        {
            LastError = string.Empty;
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(_conn) || string.IsNullOrWhiteSpace(login)) return result;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    // check UserStores table
                    using (var chk = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserStores'", c))
                    {
                        var cnt = (int)chk.ExecuteScalar();
                        if (cnt > 0)
                        {
                            using (var cmd = new SqlCommand("SELECT StoreID FROM dbo.UserStores WHERE UserID = (SELECT UserID FROM dbo.Users WHERE Login = @Login)", c))
                            {
                                cmd.Parameters.AddWithValue("@Login", login.Trim());
                                using (var rdr = cmd.ExecuteReader())
                                {
                                    while (rdr.Read())
                                    {
                                        if (!rdr.IsDBNull(0)) result.Add(rdr.GetInt32(0));
                                    }
                                }
                            }
                            return result;
                        }
                    }

                    // fallback to Users.StoreID
                    using (var cmd2 = new SqlCommand("SELECT ISNULL(StoreID,0) FROM dbo.Users WHERE Login = @Login", c))
                    {
                        cmd2.Parameters.AddWithValue("@Login", login.Trim());
                        var obj = cmd2.ExecuteScalar();
                        if (obj != null && obj != DBNull.Value)
                        {
                            int s = Convert.ToInt32(obj);
                            if (s > 0) result.Add(s);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // retry with TrustServerCertificate if TLS issues
                if (ex.Message != null && (ex.Message.Contains("??????", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var alt = _conn;
                        if (!alt.ToLowerInvariant().Contains("trustservercertificate")) alt += ";TrustServerCertificate=True";
                        using (var c2 = CreateConnectionWithOptionalTrust(alt))
                        {
                            c2.Open();
                            using (var chk = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserStores'", c2))
                            {
                                var cnt = (int)chk.ExecuteScalar();
                                if (cnt > 0)
                                {
                                    using (var cmd = new SqlCommand("SELECT StoreID FROM dbo.UserStores WHERE UserID = (SELECT UserID FROM dbo.Users WHERE Login = @Login)", c2))
                                    {
                                        cmd.Parameters.AddWithValue("@Login", login.Trim());
                                        using (var rdr = cmd.ExecuteReader())
                                        {
                                            while (rdr.Read())
                                            {
                                                if (!rdr.IsDBNull(0)) result.Add(rdr.GetInt32(0));
                                            }
                                        }
                                    }
                                    return result;
                                }
                            }

                            using (var cmd2 = new SqlCommand("SELECT ISNULL(StoreID,0) FROM dbo.Users WHERE Login = @Login", c2))
                            {
                                cmd2.Parameters.AddWithValue("@Login", login.Trim());
                                var obj = cmd2.ExecuteScalar();
                                if (obj != null && obj != DBNull.Value)
                                {
                                    int s = Convert.ToInt32(obj);
                                    if (s > 0) result.Add(s);
                                }
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        LastError = ex2.Message;
                        return result;
                    }
                }

                LastError = ex.Message;
                return result;
            }
            return result;
        }

        public int GetStoreIdForUser(string login)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(_conn) || string.IsNullOrWhiteSpace(login)) return 0;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    using (var cmd = new SqlCommand("SELECT ISNULL(StoreID,0) FROM dbo.Users WHERE Login = @Login", c))
                    {
                        cmd.Parameters.AddWithValue("@Login", login.Trim());
                        var obj = cmd.ExecuteScalar();
                        if (obj == null || obj == DBNull.Value) return 0;
                        return Convert.ToInt32(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                // if SSL trust problem, attempt to add TrustServerCertificate=True to connection string and retry
                if (ex.Message != null && ex.Message.Contains("??????", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var alt = _conn;
                        if (!alt.ToLowerInvariant().Contains("trustservercertificate")) alt += ";TrustServerCertificate=True";
                        using (var c2 = CreateConnectionWithOptionalTrust(alt))
                        {
                            c2.Open();
                            using (var cmd = new SqlCommand("SELECT ISNULL(StoreID,0) FROM dbo.Users WHERE Login = @Login", c2))
                            {
                                cmd.Parameters.AddWithValue("@Login", login.Trim());
                                var obj = cmd.ExecuteScalar();
                                if (obj == null || obj == DBNull.Value) return 0;
                                return Convert.ToInt32(obj);
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        LastError = ex2.Message;
                        return 0;
                    }
                }

                LastError = ex.Message;
                return 0;
            }
        }

        public bool UpdateProductStatus(int productId, bool status)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(_conn) || productId <= 0) return false;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    using (var cmd = new SqlCommand("UPDATE dbo.Products SET Status = @Status, UpdatedAt = GETDATE() WHERE ProductID = @ProductID", c))
                    {
                        cmd.Parameters.AddWithValue("@Status", status ? 1 : 0);
                        cmd.Parameters.AddWithValue("@ProductID", productId);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != null && (ex.Message.Contains("цепочк", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var alt = _conn;
                        if (!alt.ToLowerInvariant().Contains("trustservercertificate")) alt += ";TrustServerCertificate=True";
                        using (var c2 = CreateConnectionWithOptionalTrust(alt))
                        {
                            c2.Open();
                            using (var cmd = new SqlCommand("UPDATE dbo.Products SET Status = @Status, UpdatedAt = GETDATE() WHERE ProductID = @ProductID", c2))
                            {
                                cmd.Parameters.AddWithValue("@Status", status ? 1 : 0);
                                cmd.Parameters.AddWithValue("@ProductID", productId);
                                return cmd.ExecuteNonQuery() > 0;
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        LastError = ex2.Message;
                        return false;
                    }
                }
                LastError = ex.Message;
                return false;
            }
        }

        public bool UpdateProduct(int productId, string name, int categoryId, int manufacturerId, string desc, decimal price, decimal discount, int quantity, string imageUrl, int storeId = 0)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(_conn) || productId <= 0) return false;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    using (var tran = c.BeginTransaction())
                    {
                        try
                        {
                            using (var cmd = new SqlCommand(@"UPDATE dbo.Products SET ProductName=@Name, CategoryID=@Cat, ManufacturerID=@Man, Description=@Desc, Price=@Price, Discount=@Discount, UpdatedAt=GETDATE() WHERE ProductID=@ProductID", c, tran))
                            {
                                cmd.Parameters.AddWithValue("@Name", name ?? string.Empty);
                                cmd.Parameters.AddWithValue("@Cat", categoryId);
                                cmd.Parameters.AddWithValue("@Man", manufacturerId);
                                cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc);
                                cmd.Parameters.AddWithValue("@Price", price);
                                cmd.Parameters.AddWithValue("@Discount", discount);
                                cmd.Parameters.AddWithValue("@ProductID", productId);
                                cmd.ExecuteNonQuery();
                            }

                            // update stocks
                            string updStock = "UPDATE dbo.Stocks SET Quantity=@Qty WHERE ProductID=@ProductID" + (storeId>0?" AND StoreID=@STOREID":"");
                            using (var cmd2 = new SqlCommand(updStock, c, tran))
                            {
                                cmd2.Parameters.AddWithValue("@Qty", quantity);
                                cmd2.Parameters.AddWithValue("@ProductID", productId);
                                if (storeId>0) cmd2.Parameters.AddWithValue("@STOREID", storeId);
                                var rows = cmd2.ExecuteNonQuery();
                                if (rows==0)
                                {
                                    string ins = "INSERT INTO dbo.Stocks (ProductID, Quantity" + (storeId>0?", StoreID":"") + ") VALUES (@ProductID, @Qty" + (storeId>0?", @STOREID":"") + ")";
                                    using (var cmd3 = new SqlCommand(ins, c, tran))
                                    {
                                        cmd3.Parameters.AddWithValue("@ProductID", productId);
                                        cmd3.Parameters.AddWithValue("@Qty", quantity);
                                        if (storeId>0) cmd3.Parameters.AddWithValue("@STOREID", storeId);
                                        cmd3.ExecuteNonQuery();
                                    }
                                }
                            }

                            // update or insert image
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                using (var cmdI = new SqlCommand("UPDATE dbo.ProductImages SET ImageUrl=@Url WHERE ProductID=@ProductID AND IsMain=1", c, tran))
                                {
                                    cmdI.Parameters.AddWithValue("@Url", imageUrl);
                                    cmdI.Parameters.AddWithValue("@ProductID", productId);
                                    var r = cmdI.ExecuteNonQuery();
                                    if (r==0)
                                    {
                                        using (var cmdIns = new SqlCommand("INSERT INTO dbo.ProductImages (ProductID, ImageUrl, IsMain) VALUES (@ProductID, @Url, 1)", c, tran))
                                        {
                                            cmdIns.Parameters.AddWithValue("@ProductID", productId);
                                            cmdIns.Parameters.AddWithValue("@Url", imageUrl);
                                            cmdIns.ExecuteNonQuery();
                                        }
                                    }
                                }
                            }

                            tran.Commit();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            try { tran.Rollback(); } catch { }
                            LastError = ex.Message;
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If TLS trust issue, attempt with TrustServerCertificate
                if (ex.Message != null && (ex.Message.Contains("цепочк", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var alt = _conn;
                        if (!alt.ToLowerInvariant().Contains("trustservercertificate")) alt += ";TrustServerCertificate=True";
                        using (var c2 = CreateConnectionWithOptionalTrust(alt))
                        {
                            c2.Open();
                            using (var tran = c2.BeginTransaction())
                            {
                                try
                                {
                                    using (var cmd = new SqlCommand(@"UPDATE dbo.Products SET ProductName=@Name, CategoryID=@Cat, ManufacturerID=@Man, Description=@Desc, Price=@Price, Discount=@Discount, UpdatedAt=GETDATE() WHERE ProductID=@ProductID", c2, tran))
                                    {
                                        cmd.Parameters.AddWithValue("@Name", name ?? string.Empty);
                                        cmd.Parameters.AddWithValue("@Cat", categoryId);
                                        cmd.Parameters.AddWithValue("@Man", manufacturerId);
                                        cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(desc) ? (object)DBNull.Value : desc);
                                        cmd.Parameters.AddWithValue("@Price", price);
                                        cmd.Parameters.AddWithValue("@Discount", discount);
                                        cmd.Parameters.AddWithValue("@ProductID", productId);
                                        cmd.ExecuteNonQuery();
                                    }

                                    string updStock = "UPDATE dbo.Stocks SET Quantity=@Qty WHERE ProductID=@ProductID" + (storeId>0?" AND StoreID=@STOREID":"");
                                    using (var cmd2 = new SqlCommand(updStock, c2, tran))
                                    {
                                        cmd2.Parameters.AddWithValue("@Qty", quantity);
                                        cmd2.Parameters.AddWithValue("@ProductID", productId);
                                        if (storeId>0) cmd2.Parameters.AddWithValue("@STOREID", storeId);
                                        var rows = cmd2.ExecuteNonQuery();
                                        if (rows==0)
                                        {
                                            string ins = "INSERT INTO dbo.Stocks (ProductID, Quantity" + (storeId>0?", StoreID":"") + ") VALUES (@ProductID, @Qty" + (storeId>0?", @STOREID":"") + ")";
                                            using (var cmd3 = new SqlCommand(ins, c2, tran))
                                            {
                                                cmd3.Parameters.AddWithValue("@ProductID", productId);
                                                cmd3.Parameters.AddWithValue("@Qty", quantity);
                                                if (storeId>0) cmd3.Parameters.AddWithValue("@STOREID", storeId);
                                                cmd3.ExecuteNonQuery();
                                            }
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(imageUrl))
                                    {
                                        using (var cmdI = new SqlCommand("UPDATE dbo.ProductImages SET ImageUrl=@Url WHERE ProductID=@ProductID AND IsMain=1", c2, tran))
                                        {
                                            cmdI.Parameters.AddWithValue("@Url", imageUrl);
                                            cmdI.Parameters.AddWithValue("@ProductID", productId);
                                            var r = cmdI.ExecuteNonQuery();
                                            if (r==0)
                                            {
                                                using (var cmdIns = new SqlCommand("INSERT INTO dbo.ProductImages (ProductID, ImageUrl, IsMain) VALUES (@ProductID, @Url, 1)", c2, tran))
                                                {
                                                    cmdIns.Parameters.AddWithValue("@ProductID", productId);
                                                    cmdIns.Parameters.AddWithValue("@Url", imageUrl);
                                                    cmdIns.ExecuteNonQuery();
                                                }
                                            }
                                        }
                                    }

                                    tran.Commit();
                                    return true;
                                }
                                catch (Exception ex2)
                                {
                                    try { tran.Rollback(); } catch { }
                                    LastError = ex2.Message;
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        LastError = ex2.Message;
                        return false;
                    }
                }

                LastError = ex.Message;
                return false;
            }
        }

        public ProductViewModel? GetProductById(int productId)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(_conn) || productId <= 0) return null;
            string query = @"SELECT p.ProductID, p.ProductName, p.CategoryID, p.ManufacturerID, p.Description, p.Price, p.Discount, st.Quantity, pi.ImageUrl, p.StoreID, p.Status, p.UpdatedAt, p.CreatedAt
                                    FROM dbo.Products p
                                    LEFT JOIN dbo.Stocks st ON p.ProductID = st.ProductID
                                    LEFT JOIN dbo.ProductImages pi ON p.ProductID = pi.ProductID AND pi.IsMain = 1
                                    WHERE p.ProductID = @ProductID";
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    using (var cmd = new SqlCommand(query, c))
                    {
                        cmd.Parameters.AddWithValue("@ProductID", productId);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                var prod = new ProductViewModel();
                                prod.ProductID = rdr.IsDBNull(rdr.GetOrdinal("ProductID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ProductID"));
                                prod.ProductName = rdr.IsDBNull(rdr.GetOrdinal("ProductName")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ProductName"));
                                prod.CategoryId = rdr.IsDBNull(rdr.GetOrdinal("CategoryID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("CategoryID"));
                                prod.ManufacturerId = rdr.IsDBNull(rdr.GetOrdinal("ManufacturerID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ManufacturerID"));
                                prod.Description = rdr.IsDBNull(rdr.GetOrdinal("Description")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Description"));
                                prod.Price = rdr.IsDBNull(rdr.GetOrdinal("Price")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Price"));
                                prod.Discount = rdr.IsDBNull(rdr.GetOrdinal("Discount")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Discount"));
                                prod.Quantity = rdr.IsDBNull(rdr.GetOrdinal("Quantity")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("Quantity"));
                                prod.ImageUrl = rdr.IsDBNull(rdr.GetOrdinal("ImageUrl")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ImageUrl"));
                                prod.StoreID = rdr.IsDBNull(rdr.GetOrdinal("StoreID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("StoreID"));
                                prod.Status = rdr.IsDBNull(rdr.GetOrdinal("Status")) ? false : rdr.GetBoolean(rdr.GetOrdinal("Status"));
                                if (!rdr.IsDBNull(rdr.GetOrdinal("UpdatedAt"))) prod.UpdatedAt = rdr.GetDateTime(rdr.GetOrdinal("UpdatedAt"));
                                if (!rdr.IsDBNull(rdr.GetOrdinal("CreatedAt"))) prod.CreatedAt = rdr.GetDateTime(rdr.GetOrdinal("CreatedAt"));
                                return prod;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If TLS trust issue, attempt with TrustServerCertificate
                if (ex.Message != null && (ex.Message.Contains("цепочк", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var alt = _conn;
                        if (!alt.ToLowerInvariant().Contains("trustservercertificate")) alt += ";TrustServerCertificate=True";
                        using (var c2 = CreateConnectionWithOptionalTrust(alt))
                        {
                            c2.Open();
                            using (var cmd = new SqlCommand(query, c2))
                            {
                                cmd.Parameters.AddWithValue("@ProductID", productId);
                                using (var rdr = cmd.ExecuteReader())
                                {
                                    if (rdr.Read())
                                    {
                                        var prod = new ProductViewModel();
                                        prod.ProductID = rdr.IsDBNull(rdr.GetOrdinal("ProductID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ProductID"));
                                        prod.ProductName = rdr.IsDBNull(rdr.GetOrdinal("ProductName")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ProductName"));
                                        prod.CategoryId = rdr.IsDBNull(rdr.GetOrdinal("CategoryID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("CategoryID"));
                                        prod.ManufacturerId = rdr.IsDBNull(rdr.GetOrdinal("ManufacturerID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ManufacturerID"));
                                        prod.Description = rdr.IsDBNull(rdr.GetOrdinal("Description")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Description"));
                                        prod.Price = rdr.IsDBNull(rdr.GetOrdinal("Price")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Price"));
                                        prod.Discount = rdr.IsDBNull(rdr.GetOrdinal("Discount")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Discount"));
                                        prod.Quantity = rdr.IsDBNull(rdr.GetOrdinal("Quantity")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("Quantity"));
                                        prod.ImageUrl = rdr.IsDBNull(rdr.GetOrdinal("ImageUrl")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ImageUrl"));
                                        prod.StoreID = rdr.IsDBNull(rdr.GetOrdinal("StoreID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("StoreID"));
                                        prod.Status = rdr.IsDBNull(rdr.GetOrdinal("Status")) ? false : rdr.GetBoolean(rdr.GetOrdinal("Status"));
                                        if (!rdr.IsDBNull(rdr.GetOrdinal("UpdatedAt"))) prod.UpdatedAt = rdr.GetDateTime(rdr.GetOrdinal("UpdatedAt"));
                                        if (!rdr.IsDBNull(rdr.GetOrdinal("CreatedAt"))) prod.CreatedAt = rdr.GetDateTime(rdr.GetOrdinal("CreatedAt"));
                                        return prod;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        LastError = ex2.Message;
                        return null;
                    }
                }

                LastError = ex.Message;
                return null;
            }
            return null;
        }



        public bool AddProductToDb(string name, int categoryId, int manufacturerId, string description, decimal price, decimal discount, int quantity, string imageUrl, int storeId = 0)
        {
            LastError = string.Empty;
            if (string.IsNullOrWhiteSpace(_conn) || string.IsNullOrWhiteSpace(name)) { LastError = "Invalid parameters"; return false; }
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    using (var tran = c.BeginTransaction())
                    {
                        try
                        {
                            string ins = @"INSERT INTO dbo.Products (ProductName, CategoryID, ManufacturerID, Description, IsActive, CreatedAt, UpdatedAt, StoreID, Price, Discount, Status)
                                   VALUES (@Name, @Cat, @Man, @Desc, 1, GETDATE(), GETDATE(), @StoreID, @Price, @Discount, 0);
                                   SELECT CAST(SCOPE_IDENTITY() AS INT);";
                            int newId;
                            using (var cmd = new SqlCommand(ins, c, tran))
                            {
                                cmd.Parameters.AddWithValue("@Name", name ?? string.Empty);
                                cmd.Parameters.AddWithValue("@Cat", categoryId);
                                cmd.Parameters.AddWithValue("@Man", manufacturerId);
                                cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description);
                                cmd.Parameters.AddWithValue("@Price", price);
                                cmd.Parameters.AddWithValue("@Discount", discount);
                                cmd.Parameters.AddWithValue("@StoreID", storeId);
                                var obj = cmd.ExecuteScalar();
                                if (obj == null || obj == DBNull.Value) throw new Exception("Failed to insert product");
                                newId = Convert.ToInt32(obj);
                            }

                            // determine stocks timestamp column
                            string tsColumn = null;
                            using (var check = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Stocks' AND COLUMN_NAME='LastUpdate'", c, tran))
                            {
                                var ccount = (int)check.ExecuteScalar();
                                if (ccount > 0) tsColumn = "LastUpdate";
                                else
                                {
                                    check.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Stocks' AND COLUMN_NAME='UpdatedAt'";
                                    ccount = (int)check.ExecuteScalar();
                                    if (ccount > 0) tsColumn = "UpdatedAt";
                                }
                            }

                            // insert into Stocks
                            string insStock;
                            if (!string.IsNullOrEmpty(tsColumn))
                                insStock = (storeId > 0) ? $"INSERT INTO dbo.Stocks (ProductID, StoreID, Quantity, {tsColumn}) VALUES (@ProductID, @StoreID, @Qty, GETDATE())" : $"INSERT INTO dbo.Stocks (ProductID, Quantity, {tsColumn}) VALUES (@ProductID, @Qty, GETDATE())";
                            else
                                insStock = (storeId > 0) ? "INSERT INTO dbo.Stocks (ProductID, StoreID, Quantity) VALUES (@ProductID, @StoreID, @Qty)" : "INSERT INTO dbo.Stocks (ProductID, Quantity) VALUES (@ProductID, @Qty)";

                            using (var s = new SqlCommand(insStock, c, tran))
                            {
                                s.Parameters.AddWithValue("@ProductID", newId);
                                if (storeId > 0) s.Parameters.AddWithValue("@StoreID", storeId);
                                s.Parameters.AddWithValue("@Qty", quantity);
                                s.ExecuteNonQuery();
                            }

                            // insert product image as main
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                string insImg = "INSERT INTO dbo.ProductImages (ProductID, ImageUrl, IsMain) VALUES (@ProductID, @Url, 1)";
                                using (var im = new SqlCommand(insImg, c, tran))
                                {
                                    im.Parameters.AddWithValue("@ProductID", newId);
                                    im.Parameters.AddWithValue("@Url", string.IsNullOrWhiteSpace(imageUrl) ? (object)DBNull.Value : imageUrl);
                                    im.ExecuteNonQuery();
                                }
                            }

                            tran.Commit();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            try { tran.Rollback(); } catch { }
                            LastError = ex.Message;
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // try with TrustServerCertificate if TLS issue
                if (ex.Message != null && (ex.Message.Contains("цепочк", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var alt = _conn;
                        if (!alt.ToLowerInvariant().Contains("trustservercertificate")) alt += ";TrustServerCertificate=True";
                        using (var c2 = CreateConnectionWithOptionalTrust(alt))
                        {
                            c2.Open();
                            using (var tran = c2.BeginTransaction())
                            {
                                try
                                {
                                    string ins = @"INSERT INTO dbo.Products (ProductName, CategoryID, ManufacturerID, Description, IsActive, CreatedAt, UpdatedAt, StoreID, Price, Discount, Status)
                                   VALUES (@Name, @Cat, @Man, @Desc, 1, GETDATE(), GETDATE(), @StoreID, @Price, @Discount, 0);
                                   SELECT CAST(SCOPE_IDENTITY() AS INT);";
                                    int newId;
                                    using (var cmd = new SqlCommand(ins, c2, tran))
                                    {
                                        cmd.Parameters.AddWithValue("@Name", name ?? string.Empty);
                                        cmd.Parameters.AddWithValue("@Cat", categoryId);
                                        cmd.Parameters.AddWithValue("@Man", manufacturerId);
                                        cmd.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(description) ? (object)DBNull.Value : description);
                                        cmd.Parameters.AddWithValue("@Price", price);
                                        cmd.Parameters.AddWithValue("@Discount", discount);
                                        cmd.Parameters.AddWithValue("@StoreID", storeId);
                                        var obj = cmd.ExecuteScalar();
                                        if (obj == null || obj == DBNull.Value) throw new Exception("Failed to insert product");
                                        newId = Convert.ToInt32(obj);
                                    }

                                    // determine tsColumn
                                    string tsColumn = null;
                                    using (var check = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Stocks' AND COLUMN_NAME='LastUpdate'", c2, tran))
                                    {
                                        var ccount = (int)check.ExecuteScalar();
                                        if (ccount > 0) tsColumn = "LastUpdate";
                                        else
                                        {
                                            check.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Stocks' AND COLUMN_NAME='UpdatedAt'";
                                            ccount = (int)check.ExecuteScalar();
                                            if (ccount > 0) tsColumn = "UpdatedAt";
                                        }
                                    }

                                    string insStock;
                                    if (!string.IsNullOrEmpty(tsColumn))
                                        insStock = (storeId > 0) ? $"INSERT INTO dbo.Stocks (ProductID, StoreID, Quantity, {tsColumn}) VALUES (@ProductID, @StoreID, @Qty, GETDATE())" : $"INSERT INTO dbo.Stocks (ProductID, Quantity, {tsColumn}) VALUES (@ProductID, @Qty, GETDATE())";
                                    else
                                        insStock = (storeId > 0) ? "INSERT INTO dbo.Stocks (ProductID, StoreID, Quantity) VALUES (@ProductID, @StoreID, @Qty)" : "INSERT INTO dbo.Stocks (ProductID, Quantity) VALUES (@ProductID, @Qty)";

                                    using (var s = new SqlCommand(insStock, c2, tran))
                                    {
                                        s.Parameters.AddWithValue("@ProductID", newId);
                                        if (storeId > 0) s.Parameters.AddWithValue("@StoreID", storeId);
                                        s.Parameters.AddWithValue("@Qty", quantity);
                                        s.ExecuteNonQuery();
                                    }

                                    if (!string.IsNullOrEmpty(imageUrl))
                                    {
                                        string insImg = "INSERT INTO dbo.ProductImages (ProductID, ImageUrl, IsMain) VALUES (@ProductID, @Url, 1)";
                                        using (var im = new SqlCommand(insImg, c2, tran))
                                        {
                                            im.Parameters.AddWithValue("@ProductID", newId);
                                            im.Parameters.AddWithValue("@Url", string.IsNullOrWhiteSpace(imageUrl) ? (object)DBNull.Value : imageUrl);
                                            im.ExecuteNonQuery();
                                        }
                                    }

                                    tran.Commit();
                                    return true;
                                }
                                catch (Exception ex2)
                                {
                                    try { tran.Rollback(); } catch { }
                                    LastError = ex2.Message;
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        LastError = ex2.Message;
                        return false;
                    }
                }

                LastError = ex.Message;
                return false;
            }
        }

        // Analytics methods
        public List<OrderDto> GetOrdersByDateRange(DateTime startDate, DateTime endDate, int storeId = 0)
        {
            LastError = string.Empty;
            var result = new List<OrderDto>();
            if (string.IsNullOrWhiteSpace(_conn)) return result;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    string query = @"
        SELECT o.OrderID, o.UserID, o.ProductID, o.CreatedAt, o.Status, o.TotalAmount, o.PaymentMethod, o.DeliveryAddress, o.StoreID,
               u.Login AS UserLogin, p.ProductName
        FROM dbo.[Order] o
        INNER JOIN dbo.Users u ON o.UserID = u.UserID
        LEFT JOIN dbo.Products p ON o.ProductID = p.ProductID
        WHERE o.CreatedAt >= @StartDate AND o.CreatedAt <= @EndDate";
                    if (storeId > 0) query += " AND o.StoreID = @StoreID";
                    query += " ORDER BY o.CreatedAt DESC";

                    using (var cmd = new SqlCommand(query, c))
                    {
                        cmd.Parameters.AddWithValue("@StartDate", startDate);
                        cmd.Parameters.AddWithValue("@EndDate", endDate);
                        if (storeId > 0) cmd.Parameters.AddWithValue("@StoreID", storeId);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var od = new OrderDto
                                {
                                    OrderID = rdr.IsDBNull(rdr.GetOrdinal("OrderID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("OrderID")),
                                    UserID = rdr.IsDBNull(rdr.GetOrdinal("UserID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("UserID")),
                                    ProductID = rdr.IsDBNull(rdr.GetOrdinal("ProductID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ProductID")),
                                    CreatedAt = rdr.IsDBNull(rdr.GetOrdinal("CreatedAt")) ? DateTime.MinValue : rdr.GetDateTime(rdr.GetOrdinal("CreatedAt")),
                                    Status = rdr.IsDBNull(rdr.GetOrdinal("Status")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Status")),
                                    TotalAmount = rdr.IsDBNull(rdr.GetOrdinal("TotalAmount")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("TotalAmount")),
                                    PaymentMethod = rdr.IsDBNull(rdr.GetOrdinal("PaymentMethod")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("PaymentMethod")),
                                    DeliveryAddress = rdr.IsDBNull(rdr.GetOrdinal("DeliveryAddress")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("DeliveryAddress")),
                                    StoreID = rdr.IsDBNull(rdr.GetOrdinal("StoreID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("StoreID")),
                                    UserLogin = rdr.IsDBNull(rdr.GetOrdinal("UserLogin")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("UserLogin")),
                                    ProductName = rdr.IsDBNull(rdr.GetOrdinal("ProductName")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ProductName"))
                                };
                                result.Add(od);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return result;
        }

        public List<TopProductDto> GetTopSellingProducts(int storeId, DateTime startDate, DateTime endDate, int limit = 10)
        {
            LastError = string.Empty;
            var result = new List<TopProductDto>();
            if (string.IsNullOrWhiteSpace(_conn)) return result;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    string query = $@"
            SELECT TOP ({limit}) p.ProductID, p.ProductName, COUNT(*) AS QuantitySold, SUM(o.TotalAmount) AS Revenue
            FROM dbo.[Order] o
            INNER JOIN dbo.Products p ON o.ProductID = p.ProductID
            WHERE o.CreatedAt BETWEEN @StartDate AND @EndDate";
                    if (storeId > 0) query += " AND o.StoreID = @StoreID";
                    query += " GROUP BY p.ProductID, p.ProductName ORDER BY Revenue DESC, QuantitySold DESC";

                    using (var cmd = new SqlCommand(query, c))
                    {
                        cmd.Parameters.AddWithValue("@StartDate", startDate);
                        cmd.Parameters.AddWithValue("@EndDate", endDate);
                        if (storeId > 0) cmd.Parameters.AddWithValue("@StoreID", storeId);

                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var tp = new TopProductDto
                                {
                                    ProductID = rdr.IsDBNull(rdr.GetOrdinal("ProductID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ProductID")),
                                    ProductName = rdr.IsDBNull(rdr.GetOrdinal("ProductName")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ProductName")),
                                    QuantitySold = rdr.IsDBNull(rdr.GetOrdinal("QuantitySold")) ? 0 : Convert.ToInt32(rdr.GetValue(rdr.GetOrdinal("QuantitySold"))),
                                    Revenue = rdr.IsDBNull(rdr.GetOrdinal("Revenue")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("Revenue"))
                                };
                                result.Add(tp);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return result;
        }

        public List<AbandonedBasketDto> GetAbandonedBaskets(int storeId, int daysThreshold = 7)
        {
            LastError = string.Empty;
            var result = new List<AbandonedBasketDto>();
            if (string.IsNullOrWhiteSpace(_conn)) return result;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    string query = @"
        SELECT DISTINCT b.UserID, u.Login,
               COUNT(DISTINCT b.ProductID) as ProductCount,
               SUM(b.Quantity * p.Price * (1 - p.Discount / 100.0)) as TotalAmount
        FROM dbo.Basket b
        INNER JOIN dbo.Users u ON b.UserID = u.UserID
        INNER JOIN dbo.Products p ON b.ProductID = p.ProductID
        WHERE p.StoreID = @StoreID
          AND NOT EXISTS (
              SELECT 1 FROM dbo.[Order] o
              WHERE o.UserID = b.UserID
                AND o.ProductID = b.ProductID
                AND o.CreatedAt >= DATEADD(day, -@DaysThreshold, GETDATE())
          )
        GROUP BY b.UserID, u.Login
        ORDER BY TotalAmount DESC";

                    using (var cmd = new SqlCommand(query, c))
                    {
                        cmd.Parameters.AddWithValue("@StoreID", storeId);
                        cmd.Parameters.AddWithValue("@DaysThreshold", daysThreshold);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var ab = new AbandonedBasketDto
                                {
                                    UserID = rdr.IsDBNull(rdr.GetOrdinal("UserID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("UserID")),
                                    UserLogin = rdr.IsDBNull(rdr.GetOrdinal("Login")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Login")),
                                    ProductCount = rdr.IsDBNull(rdr.GetOrdinal("ProductCount")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ProductCount")),
                                    TotalAmount = rdr.IsDBNull(rdr.GetOrdinal("TotalAmount")) ? 0m : rdr.GetDecimal(rdr.GetOrdinal("TotalAmount"))
                                };
                                result.Add(ab);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return result;
        }

        public List<ReviewDto> GetProductReviews(int productId, bool onlyApproved = true)
        {
            LastError = string.Empty;
            var result = new List<ReviewDto>();
            if (string.IsNullOrWhiteSpace(_conn) || productId <= 0) return result;
            try
            {
                using (var c = CreateConnectionWithOptionalTrust(_conn))
                {
                    c.Open();
                    string query = @"SELECT r.ReviewID, r.UserID, u.Login, r.Title, r.ReviewText, r.Rating, r.CreatedAt, r.IsApproved FROM dbo.ProductReviews r LEFT JOIN dbo.Users u ON r.UserID = u.UserID WHERE r.ProductID = @PRODUCTID";
                    if (onlyApproved) query += " AND r.IsApproved = 1";
                    query += " ORDER BY r.CreatedAt DESC";

                    using (var cmd = new SqlCommand(query, c))
                    {
                        cmd.Parameters.AddWithValue("@PRODUCTID", productId);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                var r = new ReviewDto
                                {
                                    ReviewID = rdr.IsDBNull(rdr.GetOrdinal("ReviewID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("ReviewID")),
                                    UserID = rdr.IsDBNull(rdr.GetOrdinal("UserID")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("UserID")),
                                    UserLogin = rdr.IsDBNull(rdr.GetOrdinal("Login")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Login")),
                                    Title = rdr.IsDBNull(rdr.GetOrdinal("Title")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("Title")),
                                    ReviewText = rdr.IsDBNull(rdr.GetOrdinal("ReviewText")) ? string.Empty : rdr.GetString(rdr.GetOrdinal("ReviewText")),
                                    Rating = rdr.IsDBNull(rdr.GetOrdinal("Rating")) ? 0 : rdr.GetInt32(rdr.GetOrdinal("Rating")),
                                    CreatedAt = rdr.IsDBNull(rdr.GetOrdinal("CreatedAt")) ? DateTime.MinValue : rdr.GetDateTime(rdr.GetOrdinal("CreatedAt")),
                                    IsApproved = rdr.IsDBNull(rdr.GetOrdinal("IsApproved")) ? false : rdr.GetBoolean(rdr.GetOrdinal("IsApproved"))
                                };
                                result.Add(r);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            return result;
        }

    }
}
