-- FlashSale schema for SQL Server. Converted from MySQL DDL (TASK-025).
-- SQL Server provider: Microsoft.EntityFrameworkCore.SqlServer 8.0.10.
-- Column naming: EF Core uses PascalCase by default; explicit [Column("snake_case")] overrides
-- are declared in FlashSaleDbContext when needed for Dapper/raw-SQL compatibility.
-- All tables use NVARCHAR for Unicode support.

-- 1. ticket
IF OBJECT_ID('ticket', 'U') IS NULL
CREATE TABLE ticket (
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    Name        NVARCHAR(50) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    StartTime   DATETIME2(3) NOT NULL,
    EndTime     DATETIME2(3) NOT NULL,
    Status      INT NOT NULL DEFAULT 0,
    UpdatedAt   DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt   DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_ticket PRIMARY KEY (Id)
);

-- 2. ticket_item
IF OBJECT_ID('ticket_item', 'U') IS NULL
CREATE TABLE ticket_item (
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    Name            NVARCHAR(50) NOT NULL,
    Description     NVARCHAR(MAX) NULL,
    StockInitial    INT NOT NULL DEFAULT 0,
    StockAvailable  INT NOT NULL DEFAULT 0,
    IsStockPrepared BIT NOT NULL DEFAULT 0,
    PriceOriginal   BIGINT NOT NULL DEFAULT 0,
    PriceFlash      BIGINT NOT NULL DEFAULT 0,
    SaleStartTime   DATETIME2(3) NULL,
    SaleEndTime     DATETIME2(3) NULL,
    Status          INT NOT NULL DEFAULT 0,
    ActivityId      BIGINT NOT NULL,
    UpdatedAt       DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt       DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_ticket_item PRIMARY KEY (Id)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ticket_item_ActivityId' AND object_id = OBJECT_ID('ticket_item'))
CREATE INDEX IX_ticket_item_ActivityId ON ticket_item (ActivityId);

-- 3. ticket_order (parent — monthly shards created dynamically via app code)
IF OBJECT_ID('ticket_order', 'U') IS NULL
CREATE TABLE ticket_order (
    Id              BIGINT IDENTITY(1,1) NOT NULL,
    user_id         BIGINT NOT NULL,
    ticket_id       BIGINT NOT NULL,
    quantity        INT NOT NULL DEFAULT 0,
    order_status    INT NOT NULL DEFAULT 0,
    order_number    NVARCHAR(64) NOT NULL,
    total_amount    BIGINT NOT NULL DEFAULT 0,
    terminal_id     NVARCHAR(64) NULL,
    order_date      DATETIME2(3) NOT NULL,
    order_notes     NVARCHAR(255) NULL,
    updated_at      DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    created_at      DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_ticket_order PRIMARY KEY (Id),
    CONSTRAINT UK_ticket_order_order_number UNIQUE (order_number)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ticket_order_user_id' AND object_id = OBJECT_ID('ticket_order'))
CREATE INDEX IX_ticket_order_user_id ON ticket_order (user_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ticket_order_ticket_id' AND object_id = OBJECT_ID('ticket_order'))
CREATE INDEX IX_ticket_order_ticket_id ON ticket_order (ticket_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ticket_order_Id' AND object_id = OBJECT_ID('ticket_order'))
CREATE INDEX IX_ticket_order_Id ON ticket_order (Id);

-- NOTE: Monthly shards (ticket_order_202407 etc.) are created at runtime by the app
-- via TickerOrderRepositoryImpl. See TASK-012/TASK-013.

-- 4. order_queue (TASK-015)
IF OBJECT_ID('order_queue', 'U') IS NULL
CREATE TABLE order_queue (
    Id           BIGINT IDENTITY(1,1) NOT NULL,
    Token        NVARCHAR(64) NOT NULL,
    TicketId     INT NOT NULL,
    Quantity     INT NOT NULL,
    UserId       INT NOT NULL,
    Status       TINYINT NOT NULL DEFAULT 0,  -- 0=PENDING, 1=SUCCESS, 2=FAILED
    OrderNumber  NVARCHAR(64) NULL,
    Message      NVARCHAR(255) NULL,
    CreatedAt    DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt    DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_order_queue PRIMARY KEY (Id),
    CONSTRAINT UK_order_queue_token UNIQUE (Token)
);

-- 5. outbox_event (TASK-015)
IF OBJECT_ID('outbox_event', 'U') IS NULL
CREATE TABLE outbox_event (
    Id           BIGINT IDENTITY(1,1) NOT NULL,
    AggregateId  NVARCHAR(64) NOT NULL,  -- Token of the order — used for consumer idempotency check
    EventType    NVARCHAR(64) NOT NULL,  -- ORDER_PLACED | ...
    Payload      NVARCHAR(MAX) NOT NULL, -- JSON of PlaceOrderMqMessage
    Status       TINYINT NOT NULL DEFAULT 0,  -- 0=PENDING, 1=PUBLISHED
    CreatedAt    DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    PublishedAt  DATETIME2(3) NULL,
    CONSTRAINT PK_outbox_event PRIMARY KEY (Id)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_outbox_event_Status_CreatedAt' AND object_id = OBJECT_ID('outbox_event'))
CREATE INDEX IX_outbox_event_Status_CreatedAt ON outbox_event (Status, CreatedAt);

-- 6. idempotency_key (TASK-016)
IF OBJECT_ID('idempotency_key', 'U') IS NULL
CREATE TABLE idempotency_key (
    Token      NVARCHAR(64) NOT NULL,
    CreatedAt  DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    ExpiresAt  DATETIME2(3) NOT NULL,  -- TTL — used by cleanup job
    CONSTRAINT PK_idempotency_key PRIMARY KEY (Token)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_idempotency_key_ExpiresAt' AND object_id = OBJECT_ID('idempotency_key'))
CREATE INDEX IX_idempotency_key_ExpiresAt ON idempotency_key (ExpiresAt);

-- 7. payment_transaction (TASK-018)
-- Amount stored as DECIMAL(16,3) for fractional VND safety; gateway multiplies by 100
-- and rounds to long when signing the URL.
IF OBJECT_ID('payment_transaction', 'U') IS NULL
CREATE TABLE payment_transaction (
    Id                    BIGINT IDENTITY(1,1) NOT NULL,
    PaymentId             NVARCHAR(64) NOT NULL,  -- = vnp_TxnRef (Guid.NewGuid("N"))
    OrderNumber           NVARCHAR(50) NOT NULL,
    UserId                INT NOT NULL,
    Amount                DECIMAL(16,3) NOT NULL,
    PaymentMethod         NVARCHAR(20) NOT NULL,
    PaymentStatus         TINYINT NOT NULL DEFAULT 0,  -- 0=INIT, 1=IN_PROGRESS, 2=SUCCESS, 3=FAILED
    GatewayTransactionId   NVARCHAR(64) NULL,  -- vnp_TransactionNo on SUCCESS
    PaymentUrl            NVARCHAR(MAX) NULL,
    UpdatedAt             DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt             DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_payment_transaction PRIMARY KEY (Id),
    CONSTRAINT UK_payment_transaction_PaymentId UNIQUE (PaymentId)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transaction_OrderNumber' AND object_id = OBJECT_ID('payment_transaction'))
CREATE INDEX IX_payment_transaction_OrderNumber ON payment_transaction (OrderNumber);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transaction_OrderNumber_Status' AND object_id = OBJECT_ID('payment_transaction'))
CREATE INDEX IX_payment_transaction_OrderNumber_Status ON payment_transaction (OrderNumber, PaymentStatus);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_payment_transaction_UserId' AND object_id = OBJECT_ID('payment_transaction'))
CREATE INDEX IX_payment_transaction_UserId ON payment_transaction (UserId);

-- 8. booking (TASK-020)
-- Status values: 0=PENDING, 1=CONFIRMED, 2=CANCELLED (matches Booking entity).
-- NOTE: Java code DID NOT ship a DDL for this table (bug). We add it here for .NET parity
-- (otherwise POST /api/bookings would throw on missing table — see KNOWN_DIFFERENCES.md §26).
IF OBJECT_ID('booking', 'U') IS NULL
CREATE TABLE booking (
    Id            BIGINT IDENTITY(1,1) NOT NULL,
    TicketId      BIGINT NOT NULL,
    Quantity      INT NOT NULL,
    BookingCode   NVARCHAR(64) NOT NULL,
    Status        TINYINT NOT NULL DEFAULT 0,  -- 0=PENDING, 1=CONFIRMED, 2=CANCELLED
    CreatedAt     DATETIME2(3) NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT PK_booking PRIMARY KEY (Id),
    CONSTRAINT UK_booking_BookingCode UNIQUE (BookingCode)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_booking_TicketId' AND object_id = OBJECT_ID('booking'))
CREATE INDEX IX_booking_TicketId ON booking (TicketId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_booking_Status_CreatedAt' AND object_id = OBJECT_ID('booking'))
CREATE INDEX IX_booking_Status_CreatedAt ON booking (Status, CreatedAt);
