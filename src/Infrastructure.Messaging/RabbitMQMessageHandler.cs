﻿using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Pitstop.Infrastructure.Messaging;

public class RabbitMQMessageHandler : IMessageHandler
{
    private static readonly ActivitySource ActivitySource = new(nameof(RabbitMQMessageHandler));
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    private const int DEFAULT_PORT = 5672;
    private readonly List<string> _hosts;
    private readonly string _username;
    private readonly string _password;
    private readonly string _exchange;
    private readonly string _queuename;
    private readonly string _routingKey;
    private readonly int _port;
    private IConnection _connection;
    private IModel _model;
    private AsyncEventingBasicConsumer _consumer;
    private string _consumerTag;
    private IMessageHandlerCallback _callback;

    public RabbitMQMessageHandler(string host, string username, string password, string exchange, string queuename, string routingKey)
        : this(host, username, password, exchange, queuename, routingKey, DEFAULT_PORT)
    {
    }

    public RabbitMQMessageHandler(string host, string username, string password, string exchange, string queuename, string routingKey, int port)
        : this(new List<string>() { host }, username, password, exchange, queuename, routingKey, port)
    {
    }

    public RabbitMQMessageHandler(IEnumerable<string> hosts, string username, string password, string exchange, string queuename, string routingKey)
        : this(hosts, username, password, exchange, queuename, routingKey, DEFAULT_PORT)
    {
    }

    public RabbitMQMessageHandler(IEnumerable<string> hosts, string username, string password, string exchange, string queuename, string routingKey, int port)
    {
        _hosts = new List<string>(hosts);
        _port = port;
        _username = username;
        _password = password;
        _exchange = exchange;
        _queuename = queuename;
        _routingKey = routingKey;

        var logMessage = new StringBuilder();
        logMessage.AppendLine("Create RabbitMQ message-handler instance using config:");
        logMessage.AppendLine($" - Hosts: {string.Join(',', _hosts.ToArray())}");
        logMessage.AppendLine($" - Port: {_port}");
        logMessage.AppendLine($" - UserName: {_username}");
        logMessage.AppendLine($" - Password: {new string('*', _password.Length)}");
        logMessage.AppendLine($" - Exchange: {_exchange}");
        logMessage.AppendLine($" - Queue: {_queuename}");
        logMessage.Append($" - RoutingKey: {_routingKey}");
        Log.Information(logMessage.ToString());
    }

    public void Start(IMessageHandlerCallback callback)
    {
        _callback = callback;

        Policy
            .Handle<Exception>()
            .WaitAndRetry(9, r => TimeSpan.FromSeconds(5), (ex, ts) => { Log.Error("Error connecting to RabbitMQ. Retrying in 5 sec."); })
            .Execute(() =>
            {
                var factory = new ConnectionFactory() { UserName = _username, Password = _password, DispatchConsumersAsync = true, Port = _port };
                _connection = factory.CreateConnection(_hosts);
                _model = _connection.CreateModel();
                _model.ExchangeDeclare(_exchange, "fanout", durable: true, autoDelete: false);
                _model.QueueDeclare(_queuename, durable: true, autoDelete: false, exclusive: false);
                _model.QueueBind(_queuename, _exchange, _routingKey);
                _consumer = new AsyncEventingBasicConsumer(_model);
                _consumer.Received += Consumer_Received;
                _consumerTag = _model.BasicConsume(_queuename, false, _consumer);
            });
    }

    public void Stop()
    {
        _model.BasicCancel(_consumerTag);
        _model.Close(200, "Goodbye");
        _connection.Close();
    }

    private async Task Consumer_Received(object sender, BasicDeliverEventArgs ea)
    {
        if (await HandleEvent(ea))
        {
            _model.BasicAck(ea.DeliveryTag, false);
        }
    }

    private Task<bool> HandleEvent(BasicDeliverEventArgs ea)
    {
         // Extract the PropagationContext of the upstream parent from the message headers.
        var parentContext = Propagator.Extract(default, ea.BasicProperties, this.ExtractTraceContextFromBasicProperties);
        Baggage.Current = parentContext.Baggage;

        // Start an activity with a name following the semantic convention of the OpenTelemetry messaging specification.
        // https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md#span-name
        var activityName = $"{ea.RoutingKey} receive";
        using var activity = ActivitySource.StartActivity(activityName, ActivityKind.Consumer, parentContext.ActivityContext);

        try {
               // determine messagetype
            string messageType = Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["MessageType"]);

            // get body
            string body = Encoding.UTF8.GetString(ea.Body.ToArray());

            activity?.SetTag("message", body);

            // call callback to handle the message
            return _callback.HandleMessageAsync(messageType, body);
        }

        catch(Exception) {
            Log.Information("Message processing failed.");
            throw;
        }
     
    }

    private IEnumerable<string> ExtractTraceContextFromBasicProperties(IBasicProperties props, string key)
    {
        try
        {
            if (props.Headers.TryGetValue(key, out var value))
            {
                var bytes = value as byte[];
                return new[] { Encoding.UTF8.GetString(bytes) };
            }
        }
        catch (Exception ex)
        {
            Log.Information(ex, "Failed to extract trace context.");
        }

        return Enumerable.Empty<string>();
    }
}