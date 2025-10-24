// EmbeddingService.cs
using embeddingSync.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Text;

namespace embeddingSync.Services
{
    public class EmbeddingService
    {
        private readonly ITextEmbeddingGenerationService _embedder;
        private readonly string _connectionString;
        private const int VECTOR_DIMENSION = 768;

        // CONSTRUCTOR TO INITIALIZE EMBEDDER AND DB CONNECTION
        public EmbeddingService(string dbUrl)
        {
            var uri = new Uri(dbUrl);
            // --- EMBEDDING SETUP ---
            var kernelBuilder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0070
            kernelBuilder.AddOllamaTextEmbeddingGeneration(
                modelId: "nomic-embed-text",
                endpoint: new Uri("http://localhost:11434"),
                serviceId: "LocalEmbeddingService"
            );
#pragma warning restore SKEXP0070
            
            var kernel = kernelBuilder.Build();
            _embedder = kernel.GetRequiredService<ITextEmbeddingGenerationService>();

            // user:pass (percent-decoding handles special characters)
            var userInfo = Uri.UnescapeDataString(uri.UserInfo ?? "");
            var colonIdx = userInfo.IndexOf(':');
            var user = colonIdx >= 0 ? userInfo[..colonIdx] : userInfo;
            var pass = colonIdx >= 0 ? userInfo[(colonIdx + 1)..] : "";

            // /database
            var database = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrEmpty(database)) database = "postgres";

            // 2) Build a proper Npgsql keyword connection string
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port, // pooler Session often 5432, Transaction often 6543
                Username = user,
                Password = pass,
                Database = database,

                // Supabase requires SSL; pooler uses a proxy cert
                SslMode = SslMode.Require,
                TrustServerCertificate = true,

                // 3) Pooler safety
                MaxAutoPrepare = 0,     // Transaction pooler doesn't support prepared statements
                MaxPoolSize = 20,   // polite for serverless/batches
            };

            _connectionString = csb.ConnectionString; // e.g., postgres://user:pass@aws-...pooler.supabase.com:6543/postgres?Max%20Auto%20Prepare=0
        }

        private static string ToPgVectorLiteral(ReadOnlyMemory<float> v)
        {
            // returns something like: [0.12,0.34,...] which Postgres parses as vector via ::vector(768)
            var span = v.Span;
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < span.Length; i++)
            {
                sb.Append(span[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (i < span.Length - 1) sb.Append(',');
            }
            sb.Append(']');
            return sb.ToString();
        }


        // METHOD TO EMBED AND INSERT EXPENSE RECORDS
        public async Task EmbedAndInsertExpensesAsync(List<ExpenseRecord> expenses)
        {
            if (expenses.Count == 0)
            {
                Console.WriteLine("❌ No expense records found to insert.");
                return;
            }

            Console.WriteLine($"✅ Found {expenses.Count} expense record(s):");

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO public.expense_embeddings (expense_id, content, embedding, created_at, updated_at)
                    VALUES (@expense_id, @content, @embedding::vector(768), now(), now())
                    ON CONFLICT (expense_id) DO UPDATE
                    SET content = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        updated_at = now();";

                await using var cmd = new NpgsqlCommand(sql, conn);

                foreach (var exp in expenses)
                {
                    string sentence =
                        $"Expense ID {exp.ExpenseId}: On {exp.ExpenseDate:MMMM dd, yyyy}, " +
                        $"spent {exp.ExpenseAmount:N2} pesos for {exp.ExpenseCategory} — {exp.ExpenseDescription}. " +
                        $"Recorded by user {exp.CreatedByUserId}.";

                    var embedding = await _embedder.GenerateEmbeddingAsync(sentence);
                    var vec = ToPgVectorLiteral(embedding);

                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("expense_id", exp.ExpenseId);
                    cmd.Parameters.AddWithValue("content", sentence);
                    cmd.Parameters.AddWithValue("embedding", vec);

                    await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✅ Upserted expense_id: {exp.ExpenseId}");
                }

                Console.WriteLine("Synchronization process for expenses complete.");
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"❌ PostgreSQL Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}");
            }
        }


        // METHOD TO EMBED AND INSERT PRODUCTS RECORDS
        public async Task EmbedAndInsertProductsAsync(List<ProductRecord> products)
        {
            if (products.Count == 0)
            {
                Console.WriteLine("❌ No product records found to insert.");
                return;
            }

            Console.WriteLine($"✅ Found {products.Count} product record(s):");

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO public.product_embeddings (product_id, content, embedding, created_at, updated_at)
                    VALUES (@product_id, @content, @embedding::vector(768), now(), now())
                    ON CONFLICT (product_id) DO UPDATE
                    SET content = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        updated_at = now();";

                await using var cmd = new NpgsqlCommand(sql, conn);

                foreach (var item in products)
                {
                    string sentence =
                        $"Product '{item.ProductName}' — {item.ProductDescription}. " +
                        $"Selling price: {item.ProductPrice:N2} pesos, cost price: {item.ProductCost:N2} pesos, " +
                        $"profit margin: {(item.ProductPrice - item.ProductCost):N2} pesos. " +
                        $"Current stock: {item.CurrentStock} units, reorder point: {item.ReorderPoint} units. " +
                        $"Added on {item.CreatedAt:MMMM dd, yyyy}.";

                    var embedding = await _embedder.GenerateEmbeddingAsync(sentence);
                    var vec = ToPgVectorLiteral(embedding);

                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("product_id", item.ProductId);
                    cmd.Parameters.AddWithValue("content", sentence);
                    cmd.Parameters.AddWithValue("embedding", vec);

                    await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✅ Upserted product_id: {item.ProductId}");
                }

                Console.WriteLine("Synchronization process for products complete.");
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"❌ PostgreSQL Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}");
            }
        }


        // METHOD TO EMBED AND INSERT DEFECTIVE ITEM RECORDS
        public async Task EmbedAndInsertDefectiveItemsAsync(List<DefectiveItemRecord> defectiveItems)
        {
            if (defectiveItems.Count == 0)
            {
                Console.WriteLine("❌ No defective item records found to insert.");
                return;
            }

            Console.WriteLine($"✅ Found {defectiveItems.Count} defective item record(s):");

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO public.defective_item_embeddings (defective_item_id, content, embedding, created_at, updated_at)
                    VALUES (@defective_item_id, @content, @embedding::vector(768), now(), now())
                    ON CONFLICT (defective_item_id) DO UPDATE
                    SET content = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        updated_at = now();";

                await using var cmd = new NpgsqlCommand(sql, conn);

                foreach (var item in defectiveItems)
                {
                    string sentence =
                        $"Defective item report {item.DefectiveItemId}: On {item.ReportedDate:MMMM dd, yyyy}, " +
                        $"{item.Quantity} unit(s) of product ID {item.ProductId} were reported defective due to {item.DefectDescription}. " +
                        $"Status: {item.Status}. Reported by user {item.ReportedByUserId}.";

                    var embedding = await _embedder.GenerateEmbeddingAsync(sentence);
                    var vec = ToPgVectorLiteral(embedding);

                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("defective_item_id", item.DefectiveItemId);
                    cmd.Parameters.AddWithValue("content", sentence);
                    cmd.Parameters.AddWithValue("embedding", vec);

                    await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✅ Upserted defective_item_id: {item.DefectiveItemId}");
                }

                Console.WriteLine("Synchronization process for defective items complete.");
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"❌ PostgreSQL Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}");
            }
        }


        // METHOD TO EMBED AND INSERT ORDER ITEM RECORDS
        public async Task EmbedAndInsertOrderItemsAsync(List<OrderItemRecord> orderItems)
        {
            if (orderItems.Count == 0)
            {
                Console.WriteLine("❌ No order item records found to insert.");
                return;
            }

            Console.WriteLine($"✅ Found {orderItems.Count} order item record(s):");

            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"
                    INSERT INTO public.order_item_embeddings (order_item_id, content, embedding, created_at, updated_at)
                    VALUES (@order_item_id, @content, @embedding::vector(768), now(), now())
                    ON CONFLICT (order_item_id) DO UPDATE
                    SET content = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        updated_at = now();";

                await using var cmd = new NpgsqlCommand(sql, conn);

                foreach (var item in orderItems)
                {
                    string sentence =
                        $"Order item {item.OrderItemId}: Part of order {item.OrderId}, " +
                        $"{item.Quantity} unit(s) of product ID {item.ProductId} purchased at {item.UnitPrice:N2} pesos each, " +
                        $"subtotal {item.Subtotal:N2} pesos. Ordered on {item.CreatedAt:MMMM dd, yyyy}.";

                    var embedding = await _embedder.GenerateEmbeddingAsync(sentence);
                    var vec = ToPgVectorLiteral(embedding);

                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("order_item_id", item.OrderItemId);
                    cmd.Parameters.AddWithValue("content", sentence);
                    cmd.Parameters.AddWithValue("embedding", vec);

                    await cmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✅ Upserted order_item_id: {item.OrderItemId}");
                }

                Console.WriteLine("Synchronization process for order items complete.");
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"❌ PostgreSQL Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}");
            }
        }

    }
}





// Optimize the sentences generated for embeddings to be more concise and focused on key attributes, while still providing enough context for meaningful embeddings. Currently, the sentences lacks information and not all columns are being utilized.

// This code is part of a service that embeds various records (expenses, products, defective items, and order items) into a PostgreSQL database using vector embeddings.
// It uses the Semantic Kernel for embedding generation and Npgsql for database operations. The service handles connection setup, embedding generation, and insertion of records into the database, while also providing error handling for common issues like connection failures or missing tables.
// The embedding generation uses a local Ollama service for text embeddings, and the records are expected to have specific properties that are used to create meaningful sentences for embedding.
// The code is structured to be reusable and maintainable, with clear separation of concerns for embedding generation and database operations.
// The service methods are asynchronous, allowing for efficient handling of potentially large datasets without blocking the main thread.
// The code also includes detailed logging to track the progress of the embedding and insertion processes, making it easier to debug and monitor the service's operations.

