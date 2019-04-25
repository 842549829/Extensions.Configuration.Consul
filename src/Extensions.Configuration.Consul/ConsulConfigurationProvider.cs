using System;
using System.Text;
using System.Threading.Tasks;
using Consul;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Extensions.Configuration.Consul
{
    internal class ConsulConfigurationProvider : ConfigurationProvider, IObserver
    {
        private ConsulAgentConfiguration Configuration { get; }

        public ConsulConfigurationProvider(ConsulAgentConfiguration configuration)
        {
            Configuration = configuration;
        }

        public override void Load()
        {
            QueryConsulAsync().GetAwaiter().GetResult();
        }

        private async Task QueryConsulAsync()
        {
            using (var client = new ConsulClient(options =>
            {
                options.WaitTime = Configuration.ClientConfiguration.WaitTime;
                options.Token = Configuration.ClientConfiguration.Token;
                options.Datacenter = Configuration.ClientConfiguration.Datacenter;
                options.Address = Configuration.ClientConfiguration.Address;
            }))
            {
                var result = await client.KV.List(Configuration.QueryOptions.Folder, new QueryOptions
                {
                    Token = Configuration.ClientConfiguration.Token,
                    Datacenter = Configuration.ClientConfiguration.Datacenter
                });

                if (result.Response == null || !result.Response.Any())
                    return;

                foreach (var item in result.Response)
                {
                    item.Key = item.Key.TrimFolderPrefix(Configuration.QueryOptions.Folder);
                    if (string.IsNullOrWhiteSpace(item.Key))
                        return;
                    SetData(item);
                }
            }
        }

        private void SetData(KVPair item)
        {
            var dic = Json(item.Key, ReadValue(item.Value));
            foreach (var d in dic)
            {
                if (Data.ContainsKey(d.Key))
                {
                    Data[d.Key] = d.Value;
                }
                else
                {
                    Set(d.Key, d.Value);
                }
            }
        }

        public IDictionary<string, string> Json(string key, string val)
        {
            string jsonStr = $"{{\"{key}\":{val}}}";
            var stream = BytesToStream(Encoding.UTF8.GetBytes(jsonStr));
            return JsonConfigurationFileParser.Parse(stream);
        }

        /// <summary>
        /// 二进制数组转流
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static Stream BytesToStream(byte[] bytes)
        {
            Stream stream = new MemoryStream(bytes);
            return stream;
        }

        public string ReadValue(byte[] bytes)
        {
            return bytes != null && bytes.Length > 0
                ? Encoding.UTF8.GetString(bytes)
                : "";
        }

        public void OnChange(List<KVPair> kVs, ILogger logger)
        {
            if (kVs == null || !kVs.Any())
            {
                Data.Clear();
                OnReload();
                return;
            }

            var deleted = Data.Where(p => kVs.All(c =>
                p.Key != c.Key.TrimFolderPrefix(Configuration.QueryOptions.Folder))).ToList();

            foreach (var del in deleted)
            {
                logger.LogTrace($"Remove key [{del.Key}]");
                Data.Remove(del.Key);
            }

            foreach (var item in kVs)
            {
                item.Key = item.Key.TrimFolderPrefix(Configuration.QueryOptions.Folder);
                if (string.IsNullOrWhiteSpace(item.Key))
                    continue;
                var newValue = ReadValue(item.Value);
                if (Data.TryGetValue(item.Key, out var oldValue))
                {
                    if (oldValue == newValue)
                        continue;

                    SetData(item);
                    logger.LogTrace($"The value of key [{item.Key}] is changed from [{oldValue}] to [{newValue}]");
                }
                else
                {
                    SetData(item);
                    logger.LogTrace($"Added key [{item.Key}][{newValue}]");
                }
                OnReload();
            }
        }
    }

    public class JsonConfigurationFileParser
    {
        private JsonConfigurationFileParser() { }

        private readonly IDictionary<string, string> _data = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<string> _context = new Stack<string>();
        private string _currentPath;

        private JsonTextReader _reader;

        public static IDictionary<string, string> Parse(Stream input)
            => new JsonConfigurationFileParser().ParseStream(input);

        private IDictionary<string, string> ParseStream(Stream input)
        {
            _data.Clear();
            _reader = new JsonTextReader(new StreamReader(input));
            _reader.DateParseHandling = DateParseHandling.None;

            var jsonConfig = JObject.Load(_reader);

            VisitJObject(jsonConfig);

            return _data;
        }

        private void VisitJObject(JObject jObject)
        {
            foreach (var property in jObject.Properties())
            {
                EnterContext(property.Name);
                VisitProperty(property);
                ExitContext();
            }
        }

        private void VisitProperty(JProperty property)
        {
            VisitToken(property.Value);
        }

        private void VisitToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    VisitJObject(token.Value<JObject>());
                    break;

                case JTokenType.Array:
                    VisitArray(token.Value<JArray>());
                    break;

                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Bytes:
                case JTokenType.Raw:
                case JTokenType.Null:
                    VisitPrimitive(token.Value<JValue>());
                    break;

                default:
                    throw new FormatException(FormatError_UnsupportedJSONToken(
                        _reader.TokenType,
                        _reader.Path,
                        _reader.LineNumber,
                        _reader.LinePosition));
            }
        }

        private void VisitArray(JArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                EnterContext(index.ToString());
                VisitToken(array[index]);
                ExitContext();
            }
        }

        private void VisitPrimitive(JValue data)
        {
            var key = _currentPath;

            if (_data.ContainsKey(key))
            {
                throw new FormatException(FormatError_KeyIsDuplicated(key));
            }
            _data[key] = data.ToString(CultureInfo.InvariantCulture);
        }

        private void EnterContext(string context)
        {
            _context.Push(context);
            _currentPath = ConfigurationPath.Combine(_context.Reverse());
        }

        private void ExitContext()
        {
            _context.Pop();
            _currentPath = ConfigurationPath.Combine(_context.Reverse());
        }

        internal static string FormatError_KeyIsDuplicated(object p0)
           => string.Format(CultureInfo.CurrentCulture, GetString("Error_KeyIsDuplicated"), p0);

        internal static string FormatError_UnsupportedJSONToken(object p0, object p1, object p2, object p3)
       => string.Format(CultureInfo.CurrentCulture, GetString("Error_UnsupportedJSONToken"), p0, p1, p2, p3);

        private static string GetString(string name, params string[] formatterNames)
        {
            var value = _resourceManager.GetString(name);

            System.Diagnostics.Debug.Assert(value != null);

            if (formatterNames != null)
            {
                for (var i = 0; i < formatterNames.Length; i++)
                {
                    value = value.Replace("{" + formatterNames[i] + "}", "{" + i + "}");
                }
            }

            return value;
        }

        private static readonly ResourceManager _resourceManager
            = new ResourceManager("Microsoft.Extensions.Configuration.Json.Resources", typeof(JsonConvert).GetTypeInfo().Assembly);
    }
}
