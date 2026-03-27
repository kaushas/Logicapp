LogicAppProcessor
------------------

This folder contains a scaffolded Azure Functions project that processes inbound Service Bus messages from D365 FinOps, normalizes them using Liquid templates, and publishes canonical events.

Notes:
- Templates live in LogicApp/sktestlogicapp/Artifacts/Maps and are loaded at runtime by `LiquidMapper`.
- Inbox/Outbox persistence is represented by repository interfaces; EF Core implementation is left as TODO (do not use inline DB calls in Logic App).
- The `ManhattanPublisherStub` is a placeholder for delivery; replace with real publisher that posts to Google Pub/Sub or canonical topic as required.

Configuration placeholders:
- `FUNCTION_APP_HOST` used in Logic App workflow should be replaced with deployed Function App host.
- Service Bus connection for Logic App is referenced via `$connections.servicebus` parameters in workflow JSON.
