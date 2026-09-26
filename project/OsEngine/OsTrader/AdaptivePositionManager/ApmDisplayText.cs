namespace OsEngine.OsTrader.AdaptivePositionManager
{
    /// <summary>Russian explanations alongside stable machine reasons. Unknown future codes stay visible.</summary>
    public static class ApmDisplayText
    {
        /// <summary>Explain one recorded reason without recalculating the historical decision.</summary>
        public static string Reason(string code) => code + " — " + (code switch
        {
            "INITIAL_ENTRY" => "начальный вход по сохранённому сценарию",
            "RESTORE_AFTER_REDUCE" => "повторный набор после сокращения",
            "AVERAGE_ADVERSE" => "набор при неблагоприятном движении в пределах риска",
            "REDUCE_ON_REBOUND" => "сокращение по обратимой кривой объёма",
            "FAST_FAVORABLE_DEFER" => "обычное сокращение временно отложено",
            "FAST_ADVERSE_NO_ADD" => "быстрое движение против позиции запрещает набор",
            "NO_ADD_ZONE" => "набор запрещён вблизи неизменяемого стопа",
            "RISK_CAP" => "ограничение денежного риска",
            "MAX_VOLUME" => "достигнут предельный объём кампании",
            "REARM_PENDING" => "ожидание времени или движения цены для следующего действия",
            "PENDING_ORDER" => "ожидание исхода заявки или недостающих исполнений",
            "DATA_NOT_READY" => "данные устарели или прогрев не завершён",
            "PAUSED_NO_INCREASE" => "набор приостановлен, защита остаётся активной",
            "HARD_STOP" => "достигнут защитный уровень цены",
            "MONEY_STOP" => "исчерпан денежный бюджет всей кампании",
            "FINAL_TARGET" => "достигнута окончательная цель",
            "SESSION_EXIT" => "наступило время завершения кампании",
            "EXECUTION_UNKNOWN" => "результат команды неизвестен; резерв сохранён",
            "LATE_FILL" => "позднее исполнение после начала закрытия",
            "RECONCILIATION_REQUIRED" => "необходима подтверждённая сверка позиции и заявок",
            "INITIAL_RISK_REJECTED" => "начальный объём не проходит ограничение риска",
            "OUT_OF_ORDER" => "нарушен причинный порядок событий",
            "EMERGENCY_EXIT" => "оператор запросил аварийное закрытие",
            "MANUAL_EXIT" => "оператор запросил завершение кампании",
            "POSITION_FLAT" => "подтверждённый объём позиции равен нулю",
            "ENTRY_NOT_FILLED" => "начальный вход завершён без исполнения",
            "INVALID_FILLED_ANCHOR" => "фактическая цена входа вышла за границы сценария",
            "NUMERIC_RANGE" => "вычисление вышло за допустимый числовой диапазон",
            _ => "дополнительная причина записана в журнале"
        });
    }
}
