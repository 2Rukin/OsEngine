/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

/* Description
Диапазонный бот с масштабированием позиции по зонам (range / scale trading).

Диапазон [RangeLow; RangeHigh] делится на ZonesCount равных зон на каждую половину
от середины Middle = (RangeLow + RangeHigh) / 2.

Наращивание:
Когда цена, двигаясь от середины к краю диапазона, проходит ZoneTriggerPercent
(по умолчанию 80%) ширины очередной зоны - позиция увеличивается на VolumePerZone
контрактов. Ниже середины позиция лонговая, выше середины - шортовая.

Сокращение:
При возврате цены к середине позиция сокращается зеркально теми же порциями на тех
же ценовых уровнях и обнуляется ещё до подхода к самой середине (на уровне 80%
первой от середины зоны).

Пробой диапазона:
Ниже RangeLow и выше RangeHigh объём не наращивается - позиция удерживается в
достигнутом максимальном размере и ждёт возврата цены в диапазон.
*/

namespace OsEngine.Robots.PositionsMicromanagement
{
    [Bot("RangeZoneScaling")]
    public class RangeZoneScaling : BotPanel
    {
        private BotTabSimple _tab;

        // Базовые настройки
        private StrategyParameterString _regime;

        // Настройки диапазона
        private StrategyParameterDecimal _rangeLow;
        private StrategyParameterDecimal _rangeHigh;
        private StrategyParameterInt _zonesCount;
        private StrategyParameterDecimal _zoneTriggerPercent;

        // Настройки объёма
        private StrategyParameterDecimal _volumePerZone;

        public RangeZoneScaling(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");

            _rangeLow = CreateParameter("Range low", 100m, 0m, 1000m, 1m, "Range");
            _rangeHigh = CreateParameter("Range high", 200m, 0m, 1000m, 1m, "Range");
            _zonesCount = CreateParameter("Zones count per side", 5, 1, 50, 1, "Range");
            _zoneTriggerPercent = CreateParameter("Zone trigger percent", 80m, 50m, 95m, 5m, "Range");

            _volumePerZone = CreateParameter("Volume per zone", 1m, 1m, 50m, 1m, "Volume");

            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;

            _tab.ManualPositionSupport.DisableManualSupport();

            Description =
                "Диапазонный бот с масштабированием позиции по зонам. Диапазон [Range low; Range high] " +
                "делится на Zones count зон на каждую половину от середины. При прохождении Zone trigger " +
                "percent очередной зоны (считая от середины наружу) добавляется Volume per zone контрактов. " +
                "При возврате цены к середине позиция сокращается зеркально теми же порциями на тех же " +
                "уровнях. Ниже нижней и выше верхней границы диапазона объём не наращивается.";
        }

        // Имя стратегии в OsEngine
        public override string GetNameStrategyType()
        {
            return "RangeZoneScaling";
        }

        // Показ диалога индивидуальных настроек
        public override void ShowIndividualSettingsDialog()
        {

        }

        // Логика
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (_regime.ValueString == "Off")
            {
                return;
            }

            if (_rangeHigh.ValueDecimal <= _rangeLow.ValueDecimal
                || _zonesCount.ValueInt < 1)
            {
                return;
            }

            decimal zoneWidth = (_rangeHigh.ValueDecimal - _rangeLow.ValueDecimal) / 2 / _zonesCount.ValueInt;

            if (zoneWidth <= 0)
            {
                return;
            }

            decimal middle = (_rangeHigh.ValueDecimal + _rangeLow.ValueDecimal) / 2;
            decimal lastPrice = candles[candles.Count - 1].Close;

            if (lastPrice <= middle)
            {
                LogicLongSide(lastPrice, middle, zoneWidth);
            }
            else
            {
                LogicShortSide(lastPrice, middle, zoneWidth);
            }
        }

        // Цена ниже середины: набираем/сокращаем лонг по зонам вниз к нижней границе диапазона
        private void LogicLongSide(decimal lastPrice, decimal middle, decimal zoneWidth)
        {
            // на случай гэпа через середину - принудительно закрываем то, что осталось от шорт-стороны
            CloseAllInList(_tab.PositionOpenShort);

            bool sideAllowed = _regime.ValueString == "On" || _regime.ValueString == "OnlyLong";

            Position position = GetSinglePosition(_tab.PositionOpenLong);
            decimal currentVolume = position == null ? 0 : position.OpenVolume;

            int desiredLevel = GetDesiredLevel(middle - lastPrice, zoneWidth);
            decimal desiredVolume = desiredLevel * _volumePerZone.ValueDecimal;

            if (sideAllowed == false && desiredVolume > currentVolume)
            {
                // в запрещённых режимах (в т.ч. OnlyClosePosition) наращивание запрещено - только сокращение
                desiredVolume = currentVolume;
            }

            ApplyVolume(position, currentVolume, desiredVolume, Side.Buy);
        }

        // Цена выше середины: набираем/сокращаем шорт по зонам вверх к верхней границе диапазона
        private void LogicShortSide(decimal lastPrice, decimal middle, decimal zoneWidth)
        {
            CloseAllInList(_tab.PositionOpenLong);

            bool sideAllowed = _regime.ValueString == "On" || _regime.ValueString == "OnlyShort";

            Position position = GetSinglePosition(_tab.PositionOpenShort);
            decimal currentVolume = position == null ? 0 : position.OpenVolume;

            int desiredLevel = GetDesiredLevel(lastPrice - middle, zoneWidth);
            decimal desiredVolume = desiredLevel * _volumePerZone.ValueDecimal;

            if (sideAllowed == false && desiredVolume > currentVolume)
            {
                desiredVolume = currentVolume;
            }

            ApplyVolume(position, currentVolume, desiredVolume, Side.Sell);
        }

        // Сколько зон (от 0 до ZonesCount) пройдено от середины в текущую сторону.
        // Зона считается пройденной, когда цена преодолела ZoneTriggerPercent её ширины,
        // отсчитывая от границы зоны, ближней к середине.
        private int GetDesiredLevel(decimal distanceFromMiddle, decimal zoneWidth)
        {
            if (distanceFromMiddle <= 0)
            {
                return 0;
            }

            decimal triggerFraction = _zoneTriggerPercent.ValueDecimal / 100m;

            int level = (int)Math.Floor(distanceFromMiddle / zoneWidth - triggerFraction + 1m);

            if (level < 0)
            {
                level = 0;
            }
            else if (level > _zonesCount.ValueInt)
            {
                level = _zonesCount.ValueInt;
            }

            return level;
        }

        // Приводит объём позиции к желаемому: наращивает рыночными докупками или сокращает частичным закрытием
        private void ApplyVolume(Position position, decimal currentVolume, decimal desiredVolume, Side side)
        {
            decimal delta = desiredVolume - currentVolume;

            if (delta > 0)
            {
                if (position == null)
                {
                    if (side == Side.Buy)
                    {
                        _tab.BuyAtMarket(delta);
                    }
                    else
                    {
                        _tab.SellAtMarket(delta);
                    }
                }
                else if (side == Side.Buy)
                {
                    _tab.BuyAtMarketToPosition(position, delta);
                }
                else
                {
                    _tab.SellAtMarketToPosition(position, delta);
                }
            }
            else if (delta < 0 && position != null)
            {
                _tab.CloseAtMarket(position, -delta);
            }
        }

        private void CloseAllInList(List<Position> positions)
        {
            if (positions == null)
            {
                return;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].OpenVolume > 0)
                {
                    _tab.CloseAtMarket(positions[i], positions[i].OpenVolume);
                }
            }
        }

        private Position GetSinglePosition(List<Position> positions)
        {
            if (positions == null || positions.Count == 0)
            {
                return null;
            }

            return positions[0];
        }
    }
}
