// SupabaseDataService.cs
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using embeddingSync.Models;

namespace embeddingSync.Services
{
    public class SupabaseDataService
    {
        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _supabaseKey;


        // CONSTRUCTOR TO INITIALIZE SUPABASE KEY AND URL
        public SupabaseDataService(string supabaseUrl, string supabaseKey)
        {
            if (string.IsNullOrWhiteSpace(supabaseUrl))
                throw new ArgumentNullException(nameof(supabaseUrl), "❌ Supabase URL is null or empty. Please check your configuration.");

            if (!Uri.TryCreate(supabaseUrl, UriKind.Absolute, out var uriResult))
                throw new ArgumentException("❌ Invalid Supabase URL format. Must include https://", nameof(supabaseUrl));

            if (string.IsNullOrWhiteSpace(supabaseKey))
                throw new ArgumentNullException(nameof(supabaseKey), "❌ Supabase Key is null or empty. Please check your configuration.");

            _supabaseUrl = supabaseUrl;
            _supabaseKey = supabaseKey;

            _httpClient = new HttpClient
            {
                BaseAddress = uriResult
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseKey);
            _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseKey);
        }

        //GET EXPENSE RECORDS FROM SUPABASE
        public async Task<List<ExpenseRecord>> GetExpensesAsync(DateTime? lastSynced)
        {
            string table = "expenses";
            string uri;

            if (lastSynced.HasValue)
            {
                string encoded = HttpUtility.UrlEncode($"updated_at=gt.{lastSynced.Value:yyyy-MM-ddTHH:mm:ss}");
                uri = $"/rest/v1/{table}?{encoded}&select=*";
            }
            else
            {
                // First-time sync: fetch all rows
                uri = $"/rest/v1/{table}?select=*";
            }

            var response = await _httpClient.GetAsync(uri);

            Console.WriteLine($"📡 [HTTP] Supabase returned: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("❌ Failed to fetch data from Supabase.");
                return new List<ExpenseRecord>();
            }

            var json = await response.Content.ReadAsStringAsync();

            var data = JsonSerializer.Deserialize<List<ExpenseRecord>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return data ?? new List<ExpenseRecord>();
        }


        // GET DEFECTIVE ITEM RECORDS FROM SUPABASE
        public async Task<List<DefectiveItemRecord>> GetDefectiveRecordsAsync(DateTime? lastSynced)
        {
            // Update the method signature to accept lastSynced
            string table = "defectiveitems";
            string uri;

            if (lastSynced.HasValue)
            {
                string encoded = HttpUtility.UrlEncode($"updated_at=gt.{lastSynced.Value:yyyy-MM-ddTHH:mm:ss}");
                uri = $"/rest/v1/{table}?{encoded}&select=*";
            }
            else
            {
                uri = $"/rest/v1/{table}?select=*";
            }

            var response = await _httpClient.GetAsync(uri);
            // ... rest of the code is the same ...
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<List<DefectiveItemRecord>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return data ?? new List<DefectiveItemRecord>();
        }

        // GET PRODUCT RECORDS FROM SUPABASE
        // Inside SupabaseDataService.cs

        public async Task<List<ProductRecord>> GetProductRecordsAsync(DateTime? lastSynced)
        {
            // Update the method signature to accept lastSynced
            string table = "products";
            string uri;

            if (lastSynced.HasValue)
            {
                string encoded = HttpUtility.UrlEncode($"updated_at=gt.{lastSynced.Value:yyyy-MM-ddTHH:mm:ss}");
                uri = $"/rest/v1/{table}?{encoded}&select=*";
            }
            else
            {
                uri = $"/rest/v1/{table}?select=*";
            }

            var response = await _httpClient.GetAsync(uri);

            Console.WriteLine($"📡 [HTTP] Supabase returned for table '{table}': {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Failed to fetch data from Supabase for table '{table}'.");
                return new List<ProductRecord>();
            }

            var json = await response.Content.ReadAsStringAsync();

            var data = JsonSerializer.Deserialize<List<ProductRecord>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return data ?? new List<ProductRecord>();
        }

        // GET ORDER RECORDS FROM SUPABASE
        public async Task<List<OrderItemRecord>> GetOrderRecordsAsync(DateTime? lastSynced)
        {
            // Update the method signature to accept lastSynced
            string table = "orderitems";
            string uri;

            if (lastSynced.HasValue)
            {
                string encoded = HttpUtility.UrlEncode($"updated_at=gt.{lastSynced.Value:yyyy-MM-ddTHH:mm:ss}");
                uri = $"/rest/v1/{table}?{encoded}&select=*";
            }
            else
            {
                uri = $"/rest/v1/{table}?select=*";
            }

            var response = await _httpClient.GetAsync(uri);
            Console.WriteLine($"📡 [HTTP] Supabase returned for table '{table}': {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Failed to fetch data from Supabase for table '{table}'.");
                return new List<OrderItemRecord>();
            }
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<List<OrderItemRecord>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return data ?? new List<OrderItemRecord>();
        }
    }
}










// FIX TABLE NAMES
// The table names in the Supabase database should match the model classes.