using Npgsql;
using Pgvector;
using Pgvector.Npgsql;


namespace EmbeddingSync.Services;

public sealed class EmbeddingSyncService
{
    private readonly string _rel;
    private readonly string _vec;
    private readonly IEmbeddingProvider _emb;

    public EmbeddingSyncService(IConfiguration cfg, IEmbeddingProvider emb)
    {
        // prefer Configuration (set by Program.cs), then fall back to raw Environment
        _rel = cfg["APP__REL__CONNECTIONSTRING"]
            ?? Environment.GetEnvironmentVariable("APP__REL__CONNECTIONSTRING")
            ?? throw new Exception("APP__REL__CONNECTIONSTRING missing");

        _vec = cfg["APP__VEC__CONNECTIONSTRING"]
            ?? Environment.GetEnvironmentVariable("APP__VEC__CONNECTIONSTRING")
            ?? throw new Exception("APP__VEC__CONNECTIONSTRING missing");

        _emb = emb;

        // DEBUG: now these will actually print if something’s off
        Console.WriteLine("REL=" + _rel);
        Console.WriteLine("VEC=" + _vec);
    }

    //Products Table
    public async Task<int> BackfillProductsAsync(CancellationToken ct = default)
    {
        await using var relDb = new NpgsqlConnection(_rel);
        await relDb.OpenAsync(ct);
        await using var vecDb = new NpgsqlConnection(_vec);
        await vecDb.OpenAsync(ct);

        // Read products from REL (limit for demo)
        await using var read = new NpgsqlCommand(@"
            select p.productid, p.productname, coalesce(p.description,'') as description
            from products p
            order by p.productid
            limit 1000;", relDb);

        var items = new List<(int id, string name, string desc)>();
        await using (var rd = await read.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                items.Add((rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? "" : rd.GetString(2)));

        const string upSql = @"
            insert into product_embeddings (product_key, embedding, name, descr, updated_at)
            values (@id, @emb, @name, @desc, now())
            on conflict (product_key) do update
              set embedding=@emb, name=@name, descr=@desc, updated_at=now();";

        var count = 0;
        foreach (var (id, name, desc) in items)
        {
            ct.ThrowIfCancellationRequested();
            var text = string.IsNullOrWhiteSpace(desc) ? name : $"{name}. {desc}";
            var vec = await _emb.EmbedAsync(text, ct);
            Console.WriteLine($"Embedding length = {vec.Length} for '{name}'");

            await using var up = new NpgsqlCommand(upSql, vecDb);
            up.Parameters.AddWithValue("id", id);
            up.Parameters.AddWithValue("emb", new Vector(vec));  // ✅ no DataTypeName
            up.Parameters.AddWithValue("name", name);
            up.Parameters.AddWithValue("desc", (object?)desc ?? DBNull.Value);
            await up.ExecuteNonQueryAsync(ct);
        }
        return count;
    }

    public async Task<List<object>> QueryProductsAsync(string text, int limit = 10, CancellationToken ct = default)
    {
        var qvec = await _emb.EmbedAsync(text, ct);

        // 1) VEC: ANN (open a plain connection)
        var ids = new List<int>();
        var scores = new Dictionary<int, double>();

        await using (var vecDb = new NpgsqlConnection(_vec))
        {
            await vecDb.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
            select product_key, (1 - (embedding <#> @q)) as score
            from product_embeddings
            order by embedding <#> @q
            limit @lim;", vecDb);

            var pQ = cmd.Parameters.AddWithValue("q", new Vector(qvec));
            pQ.DataTypeName = "vector";
            cmd.Parameters.AddWithValue("lim", limit);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt32(0);
                ids.Add(id);
                scores[id] = rd.GetDouble(1);
            }
        }

        if (ids.Count == 0)
            return new List<object>();

        // 2) REL: fetch details for those ids
        var list = new List<object>();
        await using (var relDb = new NpgsqlConnection(_rel))
        {
            await relDb.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
        select productid, productname, description, supplierid, image_url
        from products
        where productid = any(@ids);", relDb);

            cmd.Parameters.AddWithValue("ids", ids);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt32(0);
                list.Add(new
                {
                    productId = id,
                    name = rd.GetString(1),
                    descr = rd.IsDBNull(2) ? null : rd.GetString(2),
                    supplier = rd.IsDBNull(3) ? (int?)null : rd.GetInt32(3),
                    imageUrl = rd.IsDBNull(4) ? null : rd.GetString(4),
                    score = scores[id]
                });
            }
        }

        return list
            .OrderByDescending(o => (double)o.GetType().GetProperty("score")!.GetValue(o)!)
            .ToList();

    }

    //Categories Table
    public async Task<int> BackfillCategoriesAsync(CancellationToken ct = default)
    {
        await using var relDb = new NpgsqlConnection(_rel);
        await relDb.OpenAsync(ct);
        await using var vecDb = new NpgsqlConnection(_vec);
        await vecDb.OpenAsync(ct);

        // Pull category rows from REL
        await using var read = new NpgsqlCommand(@"
        select
            pc.productcategoryid,           -- key
            pc.productid,                   -- for naming
            coalesce(pc.color, '')   as color,
            coalesce(pc.agesize, '') as agesize
        from productcategory pc
        order by pc.productcategoryid
        limit 5000;", relDb);

        var rows = new List<(int id, int productId, string color, string agesize)>();
        await using (var rd = await read.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                rows.Add((
                    rd.GetInt32(0),                         // productcategoryid
                    rd.GetInt32(1),                         // productid
                    rd.IsDBNull(2) ? "" : rd.GetString(2),  // color
                    rd.IsDBNull(3) ? "" : rd.GetString(3)   // agesize
                ));

        const string upSql = @"
        insert into category_embeddings (category_key, embedding, name, updated_at)
        values (@id, @emb, @name, now())
        on conflict (category_key) do update
          set embedding=@emb, name=@name, updated_at=now();";

        var count = 0;
        foreach (var (id, productId, color, agesize) in rows)
        {
            ct.ThrowIfCancellationRequested();

            // Compose a short, human-readable name for this category/variant
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(color)) parts.Add(color);
            if (!string.IsNullOrWhiteSpace(agesize)) parts.Add(agesize);
            var variant = parts.Count > 0 ? string.Join(" / ", parts) : "Unspecified";
            var name = $"Product {productId} – {variant}";

            // Use the same text for embedding (simple & consistent)
            var vec = await _emb.EmbedAsync(name, ct);

            await using var up = new NpgsqlCommand(upSql, vecDb);
            up.Parameters.AddWithValue("id", id);
            up.Parameters.AddWithValue("emb", new Vector(vec)); // ✅ pgvector binding
            up.Parameters.AddWithValue("name", name);

            count += await up.ExecuteNonQueryAsync(ct);
        }

        return count;
    }

    public async Task<List<object>> QueryCategoriesAsync(string text, int limit = 10, CancellationToken ct = default)
    {
        var qvec = await _emb.EmbedAsync(text, ct);

        var ids = new List<int>();
        var scores = new Dictionary<int, double>();

        // 1) ANN on category_embeddings
        await using (var vecDb = new NpgsqlConnection(_vec))
        {
            await vecDb.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
            select category_key, (1 - (embedding <#> @q)) as score
            from category_embeddings
            order by embedding <#> @q
            limit @lim;", vecDb);

            var pq = cmd.Parameters.AddWithValue("q", new Vector(qvec));
            pq.DataTypeName = "vector";                 // tell Npgsql this is pgvector
            cmd.Parameters.AddWithValue("lim", limit);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt32(0);
                ids.Add(id);
                scores[id] = rd.GetDouble(1);
            }
        }

        if (ids.Count == 0) return new List<object>();

        // 2) fetch category details from relational DB
        var list = new List<object>();
        await using (var relDb = new NpgsqlConnection(_rel))
        {
            await relDb.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
            select productcategoryid, trim(coalesce(color,'') || ' ' || coalesce(agesize,'')) as name
            from productcategory
            where productcategoryid = any(@ids);", relDb);

            cmd.Parameters.AddWithValue("ids", ids);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt32(0);
                list.Add(new
                {
                    categoryId = id,
                    name = rd.IsDBNull(1) ? null : rd.GetString(1),
                    score = scores[id]
                });
            }
        }

        return list.OrderByDescending(o => (double)o.GetType().GetProperty("score")!.GetValue(o)!).ToList();
    }



    //Suppliers Table
    public async Task<int> BackfillSuppliersAsync(CancellationToken ct = default)
    {
        await using var relDb = new NpgsqlConnection(_rel);
        await relDb.OpenAsync(ct);
        await using var vecDb = new NpgsqlConnection(_vec);
        await vecDb.OpenAsync(ct);

        await using var read = new NpgsqlCommand(@"
        select supplierid, suppliername, coalesce(address,'') as notes
        from suppliers
        order by supplierid
        limit 2000;", relDb);

        var rows = new List<(int id, string name, string notes)>();
        await using (var rd = await read.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                rows.Add((rd.GetInt32(0), rd.GetString(1), rd.GetString(2)));

        const string upSql = @"
        insert into supplier_embeddings (supplier_key, embedding, name, notes, updated_at)
        values (@id, @emb, @name, @notes, now())
        on conflict (supplier_key) do update
          set embedding=@emb, name=@name, notes=@notes, updated_at=now();";

        var count = 0;
        foreach (var (id, name, notes) in rows)
        {
            ct.ThrowIfCancellationRequested();
            var text = string.IsNullOrWhiteSpace(notes) ? name : $"{name}. {notes}";
            var vec = await _emb.EmbedAsync(text, ct);

            await using var up = new NpgsqlCommand(upSql, vecDb);
            up.Parameters.AddWithValue("id", id);
            var pEmb = up.Parameters.AddWithValue("emb", new Pgvector.Vector(vec));
            pEmb.DataTypeName = "vector";
            up.Parameters.AddWithValue("name", name);
            up.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
            count += await up.ExecuteNonQueryAsync(ct);
        }
        return count;
    }

    public async Task<List<object>> QuerySuppliersAsync(string text, int limit = 10, CancellationToken ct = default)
    {
        var qvec = await _emb.EmbedAsync(text, ct);

        var ids = new List<int>();
        var scores = new Dictionary<int, double>();

        // 1) ANN on supplier_embeddings
        await using (var vecDb = new NpgsqlConnection(_vec))
        {
            await vecDb.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
            select supplier_key, (1 - (embedding <#> @q)) as score
            from supplier_embeddings
            order by embedding <#> @q
            limit @lim;", vecDb);

            var pq = cmd.Parameters.AddWithValue("q", new Vector(qvec));
            pq.DataTypeName = "vector";
            cmd.Parameters.AddWithValue("lim", limit);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt32(0);
                ids.Add(id);
                scores[id] = rd.GetDouble(1);
            }
        }

        if (ids.Count == 0) return new List<object>();

        // 2) fetch supplier details from relational DB
        var list = new List<object>();
        await using (var relDb = new NpgsqlConnection(_rel))
        {
            await relDb.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(@"
            select supplierid, suppliername, address
            from suppliers
            where supplierid = any(@ids);", relDb);

            cmd.Parameters.AddWithValue("ids", ids);

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var id = rd.GetInt32(0);
                list.Add(new
                {
                    supplierId = id,
                    name = rd.GetString(1),
                    address = rd.IsDBNull(2) ? null : rd.GetString(2),
                    score = scores[id]
                });
            }
        }

        return list.OrderByDescending(o => (double)o.GetType().GetProperty("score")!.GetValue(o)!).ToList();
    }

}
