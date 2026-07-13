-- FlashSale schema. Pomelo MySQL provider uses PascalCase column names by default;
-- see FlashSaleDbContext for explicit HasColumnName overrides when needed.
CREATE TABLE IF NOT EXISTS ticket (
  id          BIGINT NOT NULL AUTO_INCREMENT,
  Name        VARCHAR(50) NOT NULL,
  Description TEXT,
  StartTime   DATETIME NOT NULL,
  EndTime     DATETIME NOT NULL,
  Status      INT DEFAULT 0,
  UpdatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP,
  CreatedAt   DATETIME DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS ticket_item (
  Id              BIGINT NOT NULL AUTO_INCREMENT,
  Name            VARCHAR(50) NOT NULL,
  Description     TEXT,
  StockInitial    INT DEFAULT 0,
  StockAvailable  INT DEFAULT 0,
  IsStockPrepared TINYINT(1) DEFAULT 0,
  PriceOriginal   BIGINT DEFAULT 0,
  PriceFlash      BIGINT DEFAULT 0,
  SaleStartTime   DATETIME,
  SaleEndTime     DATETIME,
  Status          INT DEFAULT 0,
  ActivityId      BIGINT NOT NULL,
  UpdatedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
  CreatedAt       DATETIME DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (Id),
  KEY idx_ticket_item_activity (ActivityId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
