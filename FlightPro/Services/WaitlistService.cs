using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration; // Needed for IConfiguration
using System;
using System.Collections.Generic;

public class WaitlistService
{
    private readonly string _connectionString;

    public WaitlistService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("myConnect");
    }

    public void CleanupAndPromoteForPackage(int packageId)
    {
        // 1. CLEANUP EXPIRED ITEMS
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            using (SqlTransaction transaction = conn.BeginTransaction())
            {
                try
                {
                    // A. Restore Stock
                    // LOGIC CHANGE: Both 'InCart' and 'Reserved' now expire based on p.ExpiryMinutes
                    string updateStockSql = @"
                    UPDATE pd 
                    SET pd.AvailableRooms = pd.AvailableRooms + Expired.TotalAmount
                    FROM PackageDates pd
                    JOIN (
                        SELECT b.PackageDateId, SUM(b.Amount) as TotalAmount
                        FROM Bookings b
                        JOIN PackageDates d ON b.PackageDateId = d.Id
                        JOIN Packages p ON d.PackageId = p.Id
                        WHERE d.PackageId = @PackageId 
                        AND b.Status IN ('InCart', 'Reserved')
                        AND b.CreatedAt < DATEADD(minute, -ISNULL(p.ExpiryMinutes, 15), GETDATE())
                        GROUP BY b.PackageDateId
                    ) Expired ON pd.Id = Expired.PackageDateId";

                    using (SqlCommand cmdUpdate = new SqlCommand(updateStockSql, conn, transaction))
                    {
                        cmdUpdate.Parameters.AddWithValue("@PackageId", packageId);
                        cmdUpdate.ExecuteNonQuery();
                    }

                    // B. Delete the Expired Records
                    string deleteSql = @"
                    DELETE b
                    FROM Bookings b
                    JOIN PackageDates d ON b.PackageDateId = d.Id
                    JOIN Packages p ON d.PackageId = p.Id
                    WHERE d.PackageId = @PackageId
                    AND b.Status IN ('InCart', 'Reserved')
                    -- Same unified expiry check:
                    AND b.CreatedAt < DATEADD(minute, -ISNULL(p.ExpiryMinutes, 15), GETDATE())";

                    using (SqlCommand cmdDelete = new SqlCommand(deleteSql, conn, transaction))
                    {
                        cmdDelete.Parameters.AddWithValue("@PackageId", packageId);
                        cmdDelete.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        // 2. CHECK FOR PROMOTION OPPORTUNITIES (Standard logic)
        List<int> datesWithRooms = new List<int>();

        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string getOpenDatesSql = "SELECT Id FROM PackageDates WHERE PackageId = @PkgId AND AvailableRooms > 0";
            using (SqlCommand cmd = new SqlCommand(getOpenDatesSql, conn))
            {
                cmd.Parameters.AddWithValue("@PkgId", packageId);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        datesWithRooms.Add((int)reader["Id"]);
                    }
                }
            }
        }

        // 3. RUN PROMOTION LOGIC
        foreach (int dateId in datesWithRooms)
        {
            TryPromoteFromWaitlist(dateId);
        }
    }

    public void TryPromoteFromWaitlist(int packageDateId)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            using (SqlTransaction transaction = conn.BeginTransaction())
            {
                try
                {
                    // 1. Double Check Stock (It might have changed in milliseconds)
                    int currentStock = 0;
                    string checkStockSql = "SELECT AvailableRooms FROM PackageDates WHERE Id = @DateId";
                    using (SqlCommand cmdStock = new SqlCommand(checkStockSql, conn, transaction))
                    {
                        cmdStock.Parameters.AddWithValue("@DateId", packageDateId);
                        object result = cmdStock.ExecuteScalar();
                        currentStock = (result != null && result != DBNull.Value) ? (int)result : 0;
                    }

                    if (currentStock <= 0) return; // Stop if no room

                    // 2. Find Candidate
                    string findWaitlistSql = @"
                        SELECT TOP 1 
                            w.Id, w.UserId, w.PackageId, w.RequestedAmount, 
                            pd.Price, pd.DiscountedPrice
                        FROM WaitingList w
                        JOIN PackageDates pd ON w.PackageDateId = pd.Id
                        WHERE w.PackageDateId = @DateId 
                        AND w.RequestedAmount <= @CurrentStock 
                        ORDER BY w.JoinedAt ASC";

                    int waitlistId = 0;
                    int userIdToPromote = 0;
                    int packageId = 0;
                    int amountNeeded = 0;
                    decimal finalPrice = 0;

                    using (SqlCommand cmdFind = new SqlCommand(findWaitlistSql, conn, transaction))
                    {
                        cmdFind.Parameters.AddWithValue("@DateId", packageDateId);
                        cmdFind.Parameters.AddWithValue("@CurrentStock", currentStock);

                        using (SqlDataReader reader = cmdFind.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                waitlistId = (int)reader["Id"];
                                userIdToPromote = (int)reader["UserId"];
                                packageId = (int)reader["PackageId"];
                                amountNeeded = (int)reader["RequestedAmount"];

                                decimal price = (decimal)reader["Price"];
                                decimal? discount = reader["DiscountedPrice"] != DBNull.Value ? (decimal?)reader["DiscountedPrice"] : null;
                                finalPrice = (discount.HasValue && discount.Value > 0) ? discount.Value : price;
                            }
                        }
                    }

                    if (waitlistId > 0)
                    {
                        decimal totalPrice = finalPrice * amountNeeded;

                        // 3. Book It
                        string createBookingSql = @"
                            INSERT INTO Bookings (UserId, PackageId, PackageDateId, Amount, TotalPrice, Status, CreatedAt)
                            VALUES (@UserId, @PackageId, @DateId, @Amount, @TotalPrice, 'Reserved', GETDATE())";

                        using (SqlCommand cmdBook = new SqlCommand(createBookingSql, conn, transaction))
                        {
                            cmdBook.Parameters.AddWithValue("@UserId", userIdToPromote);
                            cmdBook.Parameters.AddWithValue("@PackageId", packageId);
                            cmdBook.Parameters.AddWithValue("@DateId", packageDateId);
                            cmdBook.Parameters.AddWithValue("@Amount", amountNeeded);
                            cmdBook.Parameters.AddWithValue("@TotalPrice", totalPrice);
                            cmdBook.ExecuteNonQuery();
                        }

                        // 4. Reduce Stock
                        string reduceStockSql = "UPDATE PackageDates SET AvailableRooms = AvailableRooms - @Amount WHERE Id = @DateId";
                        using (SqlCommand cmdReduce = new SqlCommand(reduceStockSql, conn, transaction))
                        {
                            cmdReduce.Parameters.AddWithValue("@Amount", amountNeeded);
                            cmdReduce.Parameters.AddWithValue("@DateId", packageDateId);
                            cmdReduce.ExecuteNonQuery();
                        }

                        // 5. Remove from Waitlist
                        string deleteWaitlistSql = "DELETE FROM WaitingList WHERE Id = @Id";
                        using (SqlCommand cmdDel = new SqlCommand(deleteWaitlistSql, conn, transaction))
                        {
                            cmdDel.Parameters.AddWithValue("@Id", waitlistId);
                            cmdDel.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    // Optional: Log error here
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }
            }
        }
    }
}