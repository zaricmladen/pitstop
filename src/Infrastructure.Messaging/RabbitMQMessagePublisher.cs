using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Pitstop.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ implementation of the MessagePublisher.
/// </summary>
public class RabbitMQMessagePublisher : IMessagePublisher, IDisposable
{
    
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    private const int DEFAULT_PORT = 5672;
    private readonly List<string> _hosts;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly string _exchange;
    private IConnection _connection;
    private IModel _model;

    public RabbitMQMessagePublisher(string host, string username, string password, string exchange)
        : this(new List<string>() { host }, username, password, exchange, DEFAULT_PORT)
    {
    }

    public RabbitMQMessagePublisher(string host, string username, string password, string exchange, int port)
        : this(new List<string>() { host }, username, password, exchange, port)
    {
    }

    public RabbitMQMessagePublisher(IEnumerable<string> hosts, string username, string password, string exchange)
        : this(hosts, username, password, exchange, DEFAULT_PORT)
    {
    }

    public RabbitMQMessagePublisher(IEnumerable<string> hosts, string username, string password, string exchange, int port)
    {
        _hosts = new List<string>(hosts);
        _port = port;
        _username = username;
        _password = password;
        _exchange = exchange;

        var logMessage = new StringBuilder();
        logMessage.AppendLine("Create RabbitMQ message-publisher instance using config:");
        logMessage.AppendLine($" - Hosts: {string.Join(',', _hosts.ToArray())}");
        logMessage.AppendLine($" - Port: {_port}");
        logMessage.AppendLine($" - UserName: {_username}");
        logMessage.AppendLine($" - Password: {new string('*', _password.Length)}");
        logMessage.Append($" - Exchange: {_exchange}");
        Log.Information(logMessage.ToString());

        Connect();
    }

    /// <summary>
    /// Publish a message.
    /// </summary>
    /// <param name="messageType">Type of the message.</param>
    /// <param name="message">The message to publish.</param>
    /// <param name="routingKey">The routingkey to use (RabbitMQ specific).</param>
    public Task PublishMessageAsync(string messageType, object message, string routingKey, ActivitySource activitySource)
    {
        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md#span-name
        var activityName = $"{_exchange}-exchange: fanout send";
        using var activity = activitySource.StartActivity("Publish Message to RabbitMQ", ActivityKind.Producer);

        try{

            if(activity != null) {

                activity.SetTag("message.type", messageType);
                activity.SetTag("routing.key", routingKey);
            }

            var props = _model.CreateBasicProperties();
            ActivityContext contextToInject = activity?.Context ?? Activity.Current?.Context ?? default;
            Propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), props, InjectTraceContextIntoBasicProperties);
            
            return Task.Run(() =>
            {
                string data = MessageSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(data);
                IBasicProperties properties = props;
                properties.Headers = new Dictionary<string, object> { { "MessageType", messageType } };
                _model.BasicPublish(_exchange, routingKey, properties, body);
            });
            
            
        }
        catch(Exception){
            Log.Information("Message publishing failed.");
            throw;
        }
      
    }

    private void Connect()
    {
        Policy
            .Handle<Exception>()
            .WaitAndRetry(9, r => TimeSpan.FromSeconds(5), (ex, ts) => { Log.Error("Error connecting to RabbitMQ. Retrying in 5 sec."); })
            .Execute(() =>
            {
                var factory = new ConnectionFactory() { UserName = _username, Password = _password, Port = _port };
                factory.AutomaticRecoveryEnabled = true;
                _connection = factory.CreateConnection(_hosts);
                _model = _connection.CreateModel();
                _model.ExchangeDeclare(_exchange, "fanout", durable: true, autoDelete: false);
            });
    }

    public void Dispose()
    {
        _model?.Dispose();
        _model = null;
        _connection?.Dispose();
        _connection = null;
    }

    ~RabbitMQMessagePublisher()
    {
        Dispose();
    }

    private void InjectTraceContextIntoBasicProperties(IBasicProperties props, string key, string value)
    {
        try
        {
            if (props.Headers == null)
            {
                props.Headers = new Dictionary<string, object>();
            }

            props.Headers[key] = value;
        }
        catch (Exception ex)
        {
            Log.Information(ex, "Failed to inject trace context.");
        }
    }

    public Task PublishMessageAsync(string messageType, object message, string routingKey)
    {
        throw new NotImplementedException();
    }

    /* private void AddMessagingTags(Activity activity)
     {
         // These tags are added demonstrating the semantic conventions of the OpenTelemetry messaging specification
         // See:
         //   * https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md#messaging-attributes
         //   * https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/rabbitmq.md
         activity?.SetTag("messaging.system", "rabbitmq");
         activity?.SetTag("messaging.destination_kind", "queue");
         activity?.SetTag("messaging.destination", DefaultExchangeName);
         activity?.SetTag("messaging.rabbitmq.routing_key", TestQueueName);
     }*/
}