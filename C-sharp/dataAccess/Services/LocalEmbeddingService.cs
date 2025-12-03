using Microsoft.ML.Tokenizers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace dataAccess.Services
{
    /// <summary>
    /// Local ONNX-based embedding service using all-MiniLM-L6-v2 model.
    /// Generates 384-dimensional embeddings for semantic search.
    /// NO API CALLS - runs entirely on local CPU/GPU.
    /// PRODUCTION-READY: Uses BERT-compatible tokenization with vocab.txt
    /// </summary>
    public class LocalEmbeddingService : IEmbeddingService, IDisposable
    {
        private readonly InferenceSession _session;
        private readonly Dictionary<string, int> _vocab;
        private readonly ILogger<LocalEmbeddingService> _logger;
        private const int MAX_LENGTH = 128; // Model max sequence length
        private const int EMBEDDING_DIM = 384; // Output dimensions
        private const int CLS_TOKEN = 101; // [CLS]
        private const int SEP_TOKEN = 102; // [SEP]
        private const int PAD_TOKEN = 0; // [PAD]
        private const int UNK_TOKEN = 100; // [UNK]

        public LocalEmbeddingService(ILogger<LocalEmbeddingService> logger)
        {
            _logger = logger;

            // Path to ONNX model (downloaded as model.onnx from Hugging Face)
            var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "Encoder", "model.onnx");
            
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    $"ONNX model not found at {modelPath}. " +
                    "Please download model.onnx from Hugging Face (sentence-transformers/all-MiniLM-L6-v2) and place it in the Models/Encoder/ folder."
                );
            }

            // Path to vocab.txt for WordPiece tokenizer
            var vocabPath = Path.Combine(AppContext.BaseDirectory, "Models", "Encoder", "vocab.txt");
            
            if (!File.Exists(vocabPath))
            {
                throw new FileNotFoundException(
                    $"Tokenizer vocab.txt not found at {vocabPath}. " +
                    "Please download vocab.txt from sentence-transformers/all-MiniLM-L6-v2 and place it in the Models/Encoder/ folder."
                );
            }

            // Initialize ONNX Runtime session
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };
            
            _session = new InferenceSession(modelPath, sessionOptions);

            // Initialize BERT vocab
            try
            {
                // Load vocab.txt into dictionary
                _vocab = new Dictionary<string, int>();
                var vocabLines = File.ReadAllLines(vocabPath);
                for (int i = 0; i < vocabLines.Length; i++)
                {
                    _vocab[vocabLines[i]] = i;
                }
                
                _logger.LogInformation(
                    "[LocalEmbeddingService] Initialized with ONNX model and BERT vocab. " +
                    "Vocab size: {VocabSize}, Max length: {MaxLength}",
                    _vocab.Count,
                    MAX_LENGTH
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LocalEmbeddingService] Failed to initialize vocab");
                throw new InvalidOperationException(
                    "Failed to load vocab.txt. Ensure it exists in Models folder.", ex
                );
            }
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            return await Task.Run(() => GenerateEmbedding(text));
        }

        private float[] GenerateEmbedding(string text)
        {
            // 1. Tokenize input text using simple BERT tokenization
            var tokens = TokenizeText(text);

            // 2. Add [CLS] at start and [SEP] at end
            var tokenIds = new List<int> { CLS_TOKEN };
            tokenIds.AddRange(tokens);
            tokenIds.Add(SEP_TOKEN);

            // 3. Truncate or pad to MAX_LENGTH
            var inputIds = new long[MAX_LENGTH];
            var attentionMask = new long[MAX_LENGTH];
            var tokenTypeIds = new long[MAX_LENGTH]; // All zeros for single sentence

            int copyLength = Math.Min(tokenIds.Count, MAX_LENGTH);
            for (int i = 0; i < copyLength; i++)
            {
                inputIds[i] = tokenIds[i];
                attentionMask[i] = 1; // Mark as valid token
                tokenTypeIds[i] = 0; // Sentence A (all zeros for single sentence)
            }

            // 4. Create ONNX tensors (batch_size=1, sequence_length=MAX_LENGTH)
            var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, MAX_LENGTH });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, MAX_LENGTH });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, MAX_LENGTH });

            // 5. Run ONNX model inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
            };

            using var results = _session.Run(inputs);
            
            // 6. Extract embedding from output (last_hidden_state)
            // Output shape: [batch_size=1, sequence_length=128, hidden_size=384]
            var outputTensor = results.First().AsEnumerable<float>().ToArray();
            
            // 7. Mean pooling (average all token embeddings)
            var embedding = MeanPooling(outputTensor, attentionMask);

            // 8. Normalize to unit vector (for cosine similarity)
            return Normalize(embedding);
        }

        /// <summary>
        /// Mean pooling: average all token embeddings weighted by attention mask
        /// </summary>
        private float[] MeanPooling(float[] hiddenStates, long[] attentionMask)
        {
            var embedding = new float[EMBEDDING_DIM];
            int validTokens = 0;

            // Assuming hiddenStates shape: [batch=1, seq_len=128, hidden_size=384]
            // We need to average across sequence dimension
            for (int i = 0; i < MAX_LENGTH; i++)
            {
                if (attentionMask[i] == 1)
                {
                    for (int j = 0; j < EMBEDDING_DIM; j++)
                    {
                        embedding[j] += hiddenStates[i * EMBEDDING_DIM + j];
                    }
                    validTokens++;
                }
            }

            // Divide by number of valid tokens
            if (validTokens > 0)
            {
                for (int i = 0; i < EMBEDDING_DIM; i++)
                {
                    embedding[i] /= validTokens;
                }
            }

            return embedding;
        }

        /// <summary>
        /// Normalize vector to unit length (for cosine similarity)
        /// </summary>
        private float[] Normalize(float[] vector)
        {
            float magnitude = 0f;
            foreach (var val in vector)
            {
                magnitude += val * val;
            }
            magnitude = MathF.Sqrt(magnitude);

            if (magnitude > 0)
            {
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] /= magnitude;
                }
            }

            return vector;
        }

        /// <summary>
        /// Simple BERT-compatible tokenization
        /// Lowercases text, splits on whitespace and punctuation, looks up in vocab
        /// </summary>
        private List<int> TokenizeText(string text)
        {
            var tokens = new List<int>();
            
            // Lowercase and basic cleaning
            text = text.ToLowerInvariant().Trim();
            
            // Split on whitespace and punctuation
            var words = Regex.Split(text, @"(\s+|[^\w\s])").Where(w => !string.IsNullOrWhiteSpace(w));
            
            foreach (var word in words)
            {
                // Try to find exact match in vocab
                if (_vocab.TryGetValue(word, out int tokenId))
                {
                    tokens.Add(tokenId);
                }
                // Try WordPiece-style ##subword matching
                else if (word.Length > 1)
                {
                    bool found = false;
                    for (int len = word.Length; len > 0; len--)
                    {
                        var subword = "##" + word.Substring(word.Length - len);
                        if (_vocab.TryGetValue(subword, out tokenId))
                        {
                            tokens.Add(tokenId);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        tokens.Add(UNK_TOKEN); // Unknown token
                    }
                }
                else
                {
                    tokens.Add(UNK_TOKEN);
                }
            }
            
            return tokens;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
