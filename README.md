# Order API

## Overview

**Order API** là một microservice quản lý đơn hàng, được xây dựng trên .NET 8, sử dụng các mẫu kiến trúc hiện đại để đảm bảo độ tin cậy, khả năng chịu lỗi và khả năng mở rộng trong hệ thống phân tán.

## Luồng Nghiệp Vụ

### 1. Tạo Đơn Hàng
- Người dùng gửi yêu cầu tạo đơn hàng
- API xác thực thông tin đơn hàng
- Lưu đơn hàng vào database
- Tạo sự kiện `OrderCreated` và lưu vào Outbox
- Trả về thông tin đơn hàng đã tạo

### 2. Xử Lý Thanh Toán
- `OrderConsumer` nhận sự kiện `OrderCreated`
- Gọi API Payment Service để xử lý thanh toán
- Nếu thanh toán thành công:
  - Tạo sự kiện `PaymentProcessed` với `Success = true`
  - Chuyển sang bước cập nhật tồn kho
- Nếu thanh toán thất bại:
  - Tạo sự kiện `PaymentProcessed` với `Success = false`
  - Kích hoạt bồi thường (compensation)

### 3. Cập Nhật Tồn Kho
- `OrderConsumer` nhận sự kiện `PaymentProcessed` thành công
- Gọi API Inventory Service để cập nhật tồn kho
- Nếu cập nhật thành công:
  - Tạo sự kiện `InventoryUpdated` với `Success = true`
  - Chuyển sang bước xác nhận đơn hàng
- Nếu cập nhật thất bại:
  - Tạo sự kiện `InventoryUpdated` với `Success = false`
  - Kích hoạt bồi thường

### 4. Xác Nhận Đơn Hàng
- `OrderFulfilledConsumer` nhận sự kiện `InventoryUpdated` thành công
- Cập nhật trạng thái đơn hàng thành "Completed"
- Tạo sự kiện `OrderConfirmed`
- Gửi thông báo cho người dùng

### 5. Xử Lý Lỗi và Bồi Thường
- Khi có lỗi xảy ra trong bất kỳ bước nào:
  - Message được chuyển vào Dead Letter Queue
  - `DeadLetterQueueHandler` xử lý message lỗi
  - Gửi cảnh báo qua `AlertService`
  - Thực hiện bồi thường theo thứ tự ngược lại:
    1. Hoàn tiền (nếu đã thanh toán)
    2. Khôi phục tồn kho (nếu đã cập nhật)
    3. Hủy đơn hàng

### 6. Xử Lý Dead Letter Queue
- `DeadLetterQueueProcessor` kiểm tra DLQ định kỳ
- Lấy các message chưa xử lý
- Thử xử lý lại message với số lần retry tối đa
- Nếu vẫn thất bại:
  - Cập nhật trạng thái message thành "Failed"
  - Gửi cảnh báo cho quản trị viên
  - Lưu thông tin lỗi vào database

## Các Mẫu Kiến Trúc

### 1. Outbox Pattern
- Đảm bảo tính nhất quán giữa database và message queue
- Lưu sự kiện vào Outbox trong cùng transaction với đơn hàng
- `OutboxPublisherService` xử lý các message chưa được publish

### 2. Circuit Breaker Pattern
- Bảo vệ hệ thống khỏi lỗi cascade
- Cấu hình trong `CircuitBreakerPolicy`:
  - Số lần lỗi tối đa: 3
  - Thời gian break: 30 giây
  - Logging cho các trạng thái mở/đóng/half-open

### 3. Retry Pattern
- Xử lý các lỗi tạm thời
- Cấu hình trong `appsettings.json`:
  - Số lần retry: 3
  - Thời gian chờ cơ bản: 2 giây

### 4. Dead Letter Queue
- Xử lý các message không thể xử lý sau nhiều lần thử
- Cấu hình trong `appsettings.json`:
  - Thời gian tối đa: 24 giờ
  - Khoảng thời gian retry: 5 phút

## Cấu Hình

### 1. Database
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=orderdb;Username=postgres;Password=postgres"
  }
}
```

### 2. RabbitMQ
```json
{
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

### 3. Resilience
```json
{
  "Resilience": {
    "Retry": {
      "Count": 3,
      "BaseDelay": 2
    },
    "CircuitBreaker": {
      "FailureThreshold": 3,
      "MinimumThroughput": 5,
      "DurationOfBreak": 30
    },
    "DeadLetter": {
      "MaxAge": 86400,
      "RetryInterval": 300
    }
  }
}
```

## Health Checks
- Kiểm tra kết nối database
- Kiểm tra kết nối RabbitMQ
- Kiểm tra trạng thái Outbox
- Kiểm tra trạng thái Dead Letter Queue
- Kiểm tra trạng thái các background service

## Alerting
- Gửi cảnh báo qua webhook khi:
  - Message bị chuyển vào DLQ
  - Message không thể xử lý sau nhiều lần thử
  - Có lỗi nghiêm trọng trong hệ thống
- Logging chi tiết cho mọi hoạt động

## Môi Trường Phát Triển

### Yêu Cầu Hệ Thống
- .NET 8 SDK
- PostgreSQL 15+
- RabbitMQ 3.12+
- Redis 7.0+
- Docker và Docker Compose (tùy chọn)

### Cài Đặt Môi Trường

1. **Cài Đặt .NET SDK**
```bash
# Windows
winget install Microsoft.DotNet.SDK.8

# Linux
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

2. **Cài Đặt PostgreSQL**
```bash
# Windows
choco install postgresql

# Linux
sudo apt-get update
sudo apt-get install -y postgresql postgresql-contrib
```

3. **Cài Đặt RabbitMQ**
```bash
# Windows
choco install rabbitmq

# Linux
sudo apt-get update
sudo apt-get install -y rabbitmq-server
```

4. **Cài Đặt Redis**
```bash
# Windows
choco install redis-64

# Linux
sudo apt-get update
sudo apt-get install -y redis-server
```

### Cấu Hình Môi Trường

1. **Tạo File appsettings.Development.json**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=orderdb_dev;Username=postgres;Password=postgres"
  },
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  },
  "Redis": {
    "Host": "localhost",
    "Port": 6379
  }
}
```

2. **Tạo Database**
```bash
# Tạo database
createdb orderdb_dev

# Chạy migrations
dotnet ef database update
```

## Triển Khai

### 1. Triển Khai Bằng Docker

1. **Tạo Dockerfile**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["OrderApi/OrderApi.csproj", "OrderApi/"]
RUN dotnet restore "OrderApi/OrderApi.csproj"
COPY . .
WORKDIR "/src/OrderApi"
RUN dotnet build "OrderApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OrderApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderApi.dll"]
```

2. **Tạo docker-compose.yml**
```yaml
version: '3.8'

services:
  orderapi:
    build: .
    ports:
      - "5000:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=orderdb;Username=postgres;Password=postgres
      - RabbitMq__Host=rabbitmq
      - Redis__Host=redis
    depends_on:
      - postgres
      - rabbitmq
      - redis

  postgres:
    image: postgres:15
    environment:
      - POSTGRES_DB=orderdb
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
    volumes:
      - postgres_data:/var/lib/postgresql/data

  rabbitmq:
    image: rabbitmq:3.12-management
    ports:
      - "5672:5672"
      - "15672:15672"

  redis:
    image: redis:7.0
    ports:
      - "6379:6379"

volumes:
  postgres_data:
```

3. **Triển Khai**
```bash
# Build và chạy containers
docker-compose up -d

# Xem logs
docker-compose logs -f
```

### 2. Triển Khai Bằng Kubernetes

1. **Tạo deployment.yaml**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: orderapi
spec:
  replicas: 3
  selector:
    matchLabels:
      app: orderapi
  template:
    metadata:
      labels:
        app: orderapi
    spec:
      containers:
      - name: orderapi
        image: your-registry/orderapi:latest
        ports:
        - containerPort: 80
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: orderapi-secrets
              key: db-connection
        - name: RabbitMq__Host
          value: "rabbitmq-service"
        - name: Redis__Host
          value: "redis-service"
        resources:
          requests:
            memory: "256Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "200m"
        livenessProbe:
          httpGet:
            path: /health
            port: 80
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health
            port: 80
          initialDelaySeconds: 5
          periodSeconds: 5
```

2. **Tạo service.yaml**
```yaml
apiVersion: v1
kind: Service
metadata:
  name: orderapi-service
spec:
  selector:
    app: orderapi
  ports:
  - port: 80
    targetPort: 80
  type: LoadBalancer
```

3. **Triển Khai**
```bash
# Áp dụng cấu hình
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml

# Kiểm tra trạng thái
kubectl get pods
kubectl get services
```

## Giám Sát và Bảo Trì

### 1. Health Checks
```bash
# Kiểm tra sức khỏe API
curl http://localhost:5000/health

# Kiểm tra chi tiết
curl http://localhost:5000/health/detailed
```

### 2. Logging
- Logs được lưu vào file: `logs/orderapi-{date}.log`
- Cấu hình log level trong `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

### 3. Metrics
- Prometheus metrics endpoint: `/metrics`
- Grafana dashboard mẫu có sẵn trong thư mục `monitoring/`

### 4. Backup và Recovery
```bash
# Backup database
pg_dump -U postgres -d orderdb > backup.sql

# Restore database
psql -U postgres -d orderdb < backup.sql
```

## Troubleshooting

### 1. Lỗi Kết Nối Database
- Kiểm tra connection string
- Kiểm tra PostgreSQL service
- Kiểm tra network connectivity

### 2. Lỗi RabbitMQ
- Kiểm tra RabbitMQ service
- Kiểm tra credentials
- Kiểm tra queue và exchange

### 3. Lỗi Redis
- Kiểm tra Redis service
- Kiểm tra memory usage
- Kiểm tra network connectivity

### 4. Lỗi API
- Kiểm tra logs
- Kiểm tra health endpoints
- Kiểm tra metrics
