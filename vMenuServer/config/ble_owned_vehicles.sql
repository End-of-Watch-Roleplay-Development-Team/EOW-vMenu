CREATE TABLE IF NOT EXISTS `ble_owned_vehicles` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `char_id` INT NOT NULL,
  `plate` VARCHAR(8) NOT NULL,
  `model` VARCHAR(32) NOT NULL,
  `display_name` VARCHAR(128) NOT NULL,
  `saved_name` VARCHAR(64) NOT NULL,
  `vehicle_json` LONGTEXT NOT NULL,
  `created_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uniq_plate` (`plate`),
  UNIQUE KEY `uniq_char_saved_name` (`char_id`, `saved_name`),
  KEY `idx_char_id` (`char_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
