using Microsoft.Data.SqlClient;

public class BookingRuleService
{
    private readonly string _connectionString;

    public BookingRuleService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("myConnect");
    }

    // 1. Count ONLY trips that are fully confirmed and in the future
    public int GetConfirmedTripsCount(int userId)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string sql = @"
                SELECT COUNT(*) 
                FROM Bookings b
                JOIN PackageDates d ON b.PackageDateId = d.Id
                WHERE b.UserId = @UserId 
                AND b.Status = 'Confirmed' 
                AND d.StartDate > GETDATE()";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                return (int)cmd.ExecuteScalar();
            }
        }
    }

    // 2. Count items currently sitting in the cart (including Waitlist items)
    public int GetCartCount(int userId)
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();
            string sql = "SELECT COUNT(*) FROM Bookings WHERE UserId = @UserId AND Status IN ('InCart', 'Reserved')";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UserId", userId);
                return (int)cmd.ExecuteScalar();
            }
        }
    }
    public int GetTotalActiveTrips(int userId)
    {
        return GetConfirmedTripsCount(userId) + GetCartCount(userId);
    }

    // Keep this for AddToBasket (prevents manual adding if they are already full)
    public bool CanUserBookMore(int userId)
    {
        // Strict check: Confirmed + Cart must be < 3
        return (GetConfirmedTripsCount(userId) + GetCartCount(userId)) < 3;
    }
}