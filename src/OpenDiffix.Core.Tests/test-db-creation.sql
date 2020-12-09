CREATE TABLE customers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL
);

CREATE TABLE products (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    price INTEGER
);

CREATE TABLE purchases (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    customer_id INTEGER REFERENCES customers(id) NOT NULL,
    purchase_date DATETIME DEFAULT current_timestamp
);

CREATE TABLE line_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    purchase_id INTEGER REFERENCES purchases(id) NOT NULL,
    product_id INTEGER REFERENCES products(id) NOT NULL,
    quantity INTEGER NOT NULL DEFAULT 1,
    price INTEGER
);

INSERT INTO customers (id, name) values
    (1, 'Alice'), (2, 'Bob'), (3, 'Cynthia'), (4, 'Dalton'), (5, 'Elizabeth'), (6, 'Fernando'), (7, 'Gunther'),
    (8, 'Hunter'), (9, 'Isolde'), (10, 'Jonas'), (11, 'Kenneth'), (12, 'Linda'), (13, 'Martin'), (14, 'Nigel');
                                        
INSERT INTO products (id, name, price) values
    (1, 'Toast', 2), (2, 'Soda', 1), (3, 'Butter', 3), (4, 'Cinnamon', 3), (5, 'Apple', 1);

-- Customer 4 has no purchases (i.e. Dalton)
-- 38 purchases total
INSERT INTO purchases (customer_id) values
    (1), (1), (1), (1), (1), (1), (1), (1), (1), (1), (1), (1), (1), (1), (1), (1),
    (2), (2), (2), (2), (2), (2), (3), (3), (3), (3), (5), (6), (7), (8), (9), (10), (11), (12), (13), (14), (14), (14);

-- Purchase 1 lacks line items
INSERT INTO line_items (purchase_id, product_id, quantity, price) values
    -- Toast purchases
    (2, 1, 1, 2),
    (3, 1, 1, 1),
    (4, 1, 1, 2),
    (5, 1, 1, 2),
    (7, 1, 1, 3),
    (8, 1, 1, 2),
    (9, 1, 4, 2),
    (10, 1, 1, 2),
    (11, 1, 2, 2),
    (12, 1, 1, 2),
    -- Soda purchases
    ( 2, 2, 1, 1),
    ( 3, 2, 1, 1),
    ( 4, 2, 2, 1),
    ( 5, 2, 1, 1),
    ( 7, 2, 1, 1),
    ( 8, 2, 1, 1),
    ( 9, 2, 1, 1),
    (20, 2, 1, 1),
    (21, 2, 1, 1),
    (22, 2, 1, 1),
    (23, 2, 1, 1),
    (24, 2, 1, 1),
    (25, 2, 1, 1),
    (26, 2, 1, 1),
    (27, 2, 1, 1),
    (28, 2, 1, 1),
    -- Butter purchases
    ( 2, 3, 1, 3),
    ( 3, 3, 1, 3),
    ( 4, 3, 1, 3),
    ( 5, 3, 1, 3),
    (17, 3, 1, 3),
    (18, 3, 3, 3),
    (19, 3, 1, 3),
    (30, 3, 1, 3),
    (31, 3, 1, 3),
    (32, 3, 1, 3),
    (33, 3, 1, 3),
    (34, 3, 1, 3),
    (35, 3, 1, 3),
    (36, 3, 1, 3),
    (37, 3, 1, 3),
    (38, 3, 1, 3),
    -- Cinnamon purchases
    (12, 4, 1, 3),
    (13, 4, 1, 0),
    (14, 4, 1, 0),
    (15, 4, 1, 0),
    (27, 4, 1, 3),
    (28, 4, 3, 3),
    (29, 4, 1, 3),
    (30, 4, 1, 3),
    (31, 4, 1, 3),
    (32, 4, 1, 3),
    (33, 4, 1, 3),
    (34, 4, 1, 3),
    (35, 4, 1, 3),
    (36, 4, 1, 3),
    (37, 4, 1, 3),
    (38, 4, 1, 3),
    -- Apple purchases
    (12, 5, 1, 1),
    (13, 5, 1, 1),
    (14, 5, 1, 1),
    (15, 5, 2, 1),
    (27, 5, 1, 1),
    (28, 5, 1, 1),
    (29, 5, 1, 1),
    (30, 5, 1, 1),
    (31, 5, 1, 1),
    (32, 5, 2, 1),
    (33, 5, 1, 1),
    (34, 5, 1, 1),
    (35, 5, 1, 1),
    (36, 5, 1, 1),
    (37, 5, 1, 1),
    (38, 5, 1, 1);
