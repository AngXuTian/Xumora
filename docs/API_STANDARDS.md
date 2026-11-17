# API Standards

These conventions apply to new Xumora HTTP endpoints. Existing feature scaffolding should follow them as it becomes functional.

## Routes and versions

- Use URL-segment API versions: `/api/v{version}/...`.
- Use lowercase, hyphenated resource names; model operations as HTTP methods and resource paths rather than action names.
- Declare the supported version on each controller or endpoint.
- OpenAPI documents are generated per API version and exposed in Development through Swagger UI.

## Request and success responses

- Bind request DTOs and validate them with data annotations or an established validator.
- API controllers use ASP.NET Core's automatic model-state validation; invalid requests return `400` with `ValidationProblemDetails`.
- Return typed DTOs from endpoints. Return a resource directly for a single result and `PaginatedResponse<T>` for paginated collections.
- Pagination is one-based. `PaginationRequest` defaults to page 1 and 20 items, and limits page size to 100. Responses include `Items`, `Page`, `PageSize`, and `TotalCount`.
- Declare success and expected error status codes with `ProducesResponseType` so they appear in OpenAPI.

## Errors

- Use RFC 9457 `ProblemDetails` for HTTP errors and `ValidationProblemDetails` for validation failures. Do not introduce endpoint-specific error envelopes.
- Include a stable `type`, meaningful `title`, HTTP `status`, safe `detail`, and request `instance` when applicable. Do not return stack traces or sensitive provider/database details.
- The response includes the request's `X-Correlation-ID` header; unexpected server errors also include `traceId` and `correlationId` extensions.
- `ApiError` is an unused legacy contract. New endpoints must not use it as an HTTP error response.

## Naming and documentation

- Use PascalCase for C# types and properties; use camelCase in JSON (ASP.NET Core defaults).
- Name request and response DTOs for their purpose, ending in `Request` or `Response`.
- Document endpoint intent, validation constraints, response DTOs, and relevant status codes in OpenAPI.
