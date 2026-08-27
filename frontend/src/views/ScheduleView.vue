<template>
  <div>
    <el-card>
      <template #header>
        <div style="display: flex; align-items: center; justify-content: space-between">
          <span>排班查看</span>
          <el-button type="primary" :loading="loading" @click="loadAll" :disabled="!planId">查询</el-button>
        </div>
      </template>

      <el-alert v-if="errorMsg" :title="errorMsg" type="warning" closable @close="errorMsg=''" />

      <el-table ref="plansTableRef" :data="plans" v-loading="plansLoading" border stripe size="small" style="margin-bottom: 16px" highlight-current-row @current-change="selectPlan">
        <el-table-column prop="id" label="ID" width="60" />
        <el-table-column prop="planName" label="计划名称" />
        <el-table-column label="周期" width="200">
          <template #default="{ row }">{{ row.startDate }} ~ {{ row.endDate }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.status === 'PUBLISHED' ? 'success' : 'info'">{{ row.status === 'PUBLISHED' ? '已发布' : '草稿' }}</el-tag>
          </template>
        </el-table-column>
      </el-table>

      <el-radio-group v-if="planId" v-model="viewMode" @change="onModeChange" style="margin-bottom: 16px">
        <el-radio-button label="week">周视图</el-radio-button>
        <el-radio-button label="whole">整月排班</el-radio-button>
        <el-radio-button label="month">月视图</el-radio-button>
        <el-radio-button label="day">日明细</el-radio-button>
        <el-radio-button label="issues">问题详情</el-radio-button>
      </el-radio-group>

      <div v-if="viewMode === 'week' || viewMode === 'whole'" v-loading="loading">
        <el-date-picker v-if="viewMode === 'week'" v-model="weekStart" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-bottom: 12px" placeholder="选择起始日" :disabled-date="disabledDate" @change="loadWeek" />
        <div class="gantt">
          <div class="gantt-row gantt-header">
            <div class="gantt-emp-col">员工</div>
            <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
              <div class="day-label">{{ weekdayName(d.weekday) }}</div><div class="day-sub">{{ d.date }}</div>
            </div>
          </div>
          <template v-for="(row, i) in sortedWeekRows" :key="row.employeeId">
            <!-- 全职员工与兼职员工之间的分隔行 -->
            <div v-if="i === firstPartTimeIndex" class="gantt-divider">
              <span class="gantt-divider-badge">兼</span>兼职员工
            </div>
            <div class="gantt-row" :class="{ 'gantt-row-parttime': row.isParttime === 1 }">
              <div class="gantt-emp-col"><div class="emp-name">{{ row.employeeName }}<el-tag v-if="row.isParttime === 1" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div class="emp-sub">{{ row.department }}</div></div>
              <div v-for="d in weekDays" :key="d.date" class="gantt-day-col">
                <template v-if="getWeekDay(row, d.date)">
                  <div v-if="getWeekDay(row, d.date).isRestDay === 1 && row.isParttime !== 1" class="day-block rest-block">休</div>
                  <div v-else-if="getWeekDay(row, d.date).isRestDay === 1" class="day-block empty-block"></div>
                  <div v-else class="day-block work-block">
                    <div class="shift-code">{{ getWeekDay(row, d.date).shiftCode || '班' }}</div>
                    <div class="shift-time">{{ fmt(getWeekDay(row, d.date).startTime) }}-{{ fmt(getWeekDay(row, d.date).endTime) }}</div>
                    <div
                      v-if="getWeekDay(row, d.date).breakStartTime && row.isParttime !== 1"
                      class="shift-break"
                      :title="getWeekDay(row, d.date).breakCoverEmployeeName
                        ? `休息 ${fmt(getWeekDay(row, d.date).breakStartTime)}-${fmt(getWeekDay(row, d.date).breakEndTime)}，由 ${getWeekDay(row, d.date).breakCoverEmployeeName} 顶班`
                        : `休息 ${fmt(getWeekDay(row, d.date).breakStartTime)}-${fmt(getWeekDay(row, d.date).breakEndTime)}`"
                    >
                      休 {{ fmt(getWeekDay(row, d.date).breakStartTime) }}-{{ fmt(getWeekDay(row, d.date).breakEndTime) }}{{ getWeekDay(row, d.date).breakCoverEmployeeName ? ' · ' + getWeekDay(row, d.date).breakCoverEmployeeName + ' 顶' : '' }}
                    </div>
                  </div>
                </template>
                <div v-else class="day-block empty-block"></div>
              </div>
            </div>
          </template>
        </div>

        <!-- 兼职替补需求色块：低技能岗位缺口（仅周视图展示，整月视图横向过长） -->
        <div v-if="viewMode === 'week' && weekPartTimeNeeds.length" class="parttime-block" style="margin-top: 16px">
          <div class="parttime-title">
            <span class="parttime-badge">兼</span>
            兼职替补需求（低技能岗位缺口，建议寻找兼职人员临时填补）
          </div>
          <div class="parttime-table">
            <div class="parttime-row parttime-header">
              <div class="parttime-ws-col">岗位</div>
              <div v-for="d in weekDays" :key="d.date" class="parttime-day-col">{{ d.date.substring(5) }}</div>
            </div>
            <div v-for="need in weekPartTimeNeeds" :key="need.workstationName + (need.days || '')" class="parttime-row">
              <div class="parttime-ws-col">{{ need.workstationName }}</div>
              <div
                v-for="d in weekDays"
                :key="d.date"
                class="parttime-day-col"
                :class="{ active: hasPartTimeNeed(need.workstationName, d.date) }"
                :title="hasPartTimeNeed(need.workstationName, d.date) ? `${need.workstationName} ${d.date} 缺口，建议找兼职替补` : ''"
              ></div>
            </div>
          </div>
          <div class="parttime-legend">
            <span class="legend-box parttime-legend-box"></span> 该日该低技能岗位存在缺口 → 建议寻找兼职人员临时替补
          </div>
        </div>
      </div>

      <div v-if="viewMode === 'month'" v-loading="loading">
        <div class="calendar">
          <div class="cal-header">
            <div v-for="w in ['周一','周二','周三','周四','周五','周六','周日']" :key="w" class="cal-header-cell">{{ w }}</div>
          </div>
          <div v-for="(week, wi) in weeks" :key="wi" class="cal-week">
            <div v-for="(day, di) in week" :key="day.date || `empty-${wi}-${di}`" class="cal-cell" :class="{ 'is-empty': !day.date, 'is-selected': day.date === selectedDate }" @click="day.date && selectDate(day.date)">
              <template v-if="day.date">
                <div class="cal-day-num">{{ day.dayNum }}</div>
                <div class="cal-work">{{ day.workCount }} 上班</div>
                <div class="cal-parttime" v-if="day.partTimeWorkCount > 0">兼职 {{ day.partTimeWorkCount }} 上班</div>
                <div class="cal-rest" v-if="day.restCount > 0">{{ day.restCount }} 休息</div>
                <div class="cal-shift" v-if="day.shiftSummary">{{ day.shiftSummary }}</div>
              </template>
            </div>
          </div>
        </div>
        <div v-if="selectedDate" style="margin-top: 24px">
          <el-divider content-position="left">{{ selectedDate }}</el-divider>
          <div class="batch-bar">
            <el-switch v-model="batchMode" size="small" />
            <span class="batch-label">批量模式（框选多格→批量改为休息；拖拽/双击在批量模式下禁用）</span>
            <el-button v-if="batchMode && batchSelect.active" size="small" type="danger" link @click="commitBatchRest">将选中区域改为休息（{{ batchTargetCount }} 格）</el-button>
            <el-button v-if="batchMode" size="small" link @click="clearBatchSelect">清除选区</el-button>
          </div>
          <div v-loading="dailyLoading" class="matrix-wrap"><div class="matrix" :class="{ 'has-chip-highlight': highlightedEmpId, 'batch-mode': batchMode }" @click="highlightedEmpId = null" @mousedown.capture="onBatchMouseDown" @mouseup.capture="onBatchMouseUp">
            <div class="m-row m-header"><div class="m-ws-col">工作站</div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display"><span v-if="isHour(slot)">{{ slot.display }}</span></div></div>
            <div v-for="ws in dailyWorkstations" :key="ws" class="m-row"><div class="m-ws-col">{{ ws }}<el-tag v-if="wsLowSkill(ws)" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div v-for="(slot, si) in slots" :key="slot.key" class="m-slot-col" :class="[cellClass(ws, slot), snapTarget.ws === ws && snapTarget.slotIdx === si ? 'snap-target' : '', batchSelected(ws, si) ? 'batch-selected' : '']" :data-slot="slot.key"><div v-if="dailySlotIssues(ws, slot).length" class="gap-flag" :class="{ 'gap-flag-low': lowSkillGapIssues(ws, slot).length > 0 }" :title="gapTooltip(ws, slot)">{{ lowSkillGapIssues(ws, slot).length > 0 ? '兼' : '缺' }}</div><div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip" :class="{ 'is-parttime': emp.isParttime === 1, 'pt-first': isFirstPartTimeChip(emp, ws, slot), 'is-break': inBreak(emp, slot), 'is-highlighted': highlightedEmpId === emp.employeeId }" :title="'单击高亮该员工当天全部色块；双击切换休息/上班；按住拖动可移动半小时'" @mousedown.prevent.stop="onChipMouseDown($event, emp, ws, slot)" @click.stop="onChipClick(emp, slot)" @dblclick.stop="onChipDblClick(emp, slot)"><div class="emp-name">{{ emp.employeeName }}<span v-if="prefMatch(emp, ws)" class="pref-dot" title="与店长历史偏好一致" /> <span v-if="inBreak(emp, slot)" class="break-flag" :title="breakTip(emp)">休</span></div><div v-if="!inBreak(emp, slot)" class="emp-shift">{{ emp.shiftCode || '--' }}</div><div v-else class="emp-shift break-info" :title="breakTip(emp)">休息</div></div></div></div>
          </div></div>
        </div>
      </div>

      <div v-if="viewMode === 'day'" v-loading="loading">
        <el-date-picker v-model="dayDate" type="date" value-format="YYYY-MM-DD" style="width: 150px; margin-bottom: 12px" placeholder="选择日期" :disabled-date="disabledDate" @change="loadDay" />
        <div v-if="dayDate" class="matrix-wrap"><div class="matrix" :class="{ 'has-chip-highlight': highlightedEmpId }" @click="highlightedEmpId = null">
          <div v-if="rangeSelect.active" class="range-hint">{{ rangeHint }}</div>
          <div v-if="rangeSel.visible" class="range-toolbar" @click.stop>
            <span class="rt-info">{{ rangeSel.ws }} {{ rangeSelStart }} ~ {{ rangeSelEnd }}（{{ rangeSelCount }} 段）</span>
            <el-button size="small" type="primary" @click="openAddDialog">加人</el-button>
            <el-button size="small" type="primary" plain @click="openReplaceDialog">换人</el-button>
            <el-button size="small" @click="moveRangeBy(-30)">◀ 左移</el-button>
            <el-button size="small" @click="moveRangeBy(30)">右移 ▶</el-button>
            <el-select v-model="rangeSel.restEmployeeId" size="small" placeholder="员工" style="width: 110px">
              <el-option v-for="e in rangeSelEmployees" :key="e.id" :value="e.id" :label="e.name" />
            </el-select>
            <el-button size="small" type="warning" :disabled="!rangeSel.restEmployeeId" @click="toggleRangeRest">{{ rangeSelRestLabel }}</el-button>
            <el-button size="small" type="danger" @click="cancelRangeSchedule">取消排班{{ rangeSel.restEmployeeId ? '' : '（全部）' }}</el-button>
            <el-button size="small" link @click="clearRangeSel">✕</el-button>
          </div>
          <div class="m-row m-header"><div class="m-ws-col">工作站</div><div v-for="slot in slots" :key="slot.key" class="m-slot-col" :title="slot.display"><span v-if="isHour(slot)">{{ slot.display }}</span></div></div>
          <div v-for="ws in dailyWorkstations" :key="ws" class="m-row"><div class="m-ws-col">{{ ws }}<el-tag v-if="wsLowSkill(ws)" type="success" size="small" style="margin-left:4px">兼</el-tag></div><div v-for="(slot, si) in slots" :key="slot.key" class="m-slot-col" :class="[cellClass(ws, slot), { 'is-empty': dailyCellUsers(ws, slot).length === 0 }, (rangeSelected(ws, slot) || rangeSelSelected(ws, slot)) ? 'range-selected' : '', snapTarget.ws === ws && snapTarget.slotIdx === si ? 'snap-target' : '', batchSelected(ws, si) ? 'batch-selected' : '']" :data-slot="slot.key" :title="dailyCellUsers(ws, slot).length === 0 ? '点击添加人员，按住滑动可选多个时段' : '按住滑动可选择范围后操作（加人/换人/平移/休息）'" @mousedown="onCellMouseDown(ws, slot, $event)" @click="onCellClick(ws, slot)"><div v-if="dailySlotIssues(ws, slot).length" class="gap-flag" :class="{ 'gap-flag-low': lowSkillGapIssues(ws, slot).length > 0 }" :title="gapTooltip(ws, slot)">{{ lowSkillGapIssues(ws, slot).length > 0 ? '兼' : '缺' }}</div><div v-for="emp in dailyCellUsers(ws, slot)" :key="emp.employeeId" class="emp-chip" :class="{ 'is-parttime': emp.isParttime === 1, 'pt-first': isFirstPartTimeChip(emp, ws, slot), 'is-break': inBreak(emp, slot), 'is-highlighted': highlightedEmpId === emp.employeeId }" :title="'单击高亮该员工当天全部色块；双击切换休息/上班；按住滑动可选择范围'" @click.stop="onChipClick(emp, slot)" @dblclick.stop="onChipDblClick(emp, slot)"><div class="emp-name">{{ emp.employeeName }}<span v-if="prefMatch(emp, ws)" class="pref-dot" title="与店长历史偏好一致" /> <span v-if="inBreak(emp, slot)" class="break-flag" :title="breakTip(emp)">休</span></div><div v-if="!inBreak(emp, slot)" class="emp-shift">{{ emp.shiftCode || '--' }}</div><div v-else class="emp-shift break-info" :title="breakTip(emp)">休息</div></div></div></div>
        </div></div>
      </div>

      <div v-if="viewMode === 'issues'" v-loading="issuesLoading">
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num">{{ issuesList.length }}</div><div class="stat-label">问题总数</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#f56c6c">{{ issueStats.gapCount }}</div><div class="stat-label">岗位缺口</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#e6a23c">{{ issueStats.warnCount }}</div><div class="stat-label">警告级别</div></el-card></el-col>
          <el-col :span="6"><el-card shadow="hover"><div class="stat-num" style="color:#67c23a">{{ issueStats.daysCount }}</div><div class="stat-label">影响天数</div></el-card></el-col>
        </el-row>
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="24">
            <el-card header="问题分析">
              <div style="display: flex; align-items: flex-start; gap: 12px; flex-wrap: wrap">
                <div style="display: flex; align-items: center; gap: 8px">
                  <svg viewBox="0 0 200 200" width="220" height="220">
                    <circle v-for="(slice, i) in typePieData" :key="i" :cx="100" :cy="100" :r="80" fill="none" :stroke="slice.color" stroke-width="30" :stroke-dasharray="`${slice.pct * 502.65} ${(1 - slice.pct) * 502.65}`" :stroke-dashoffset="(typePieOffset[i])" transform="rotate(-90 100 100)" style="cursor: pointer" @click="filterTableByType(slice.key)" />
                    <text v-for="(slice, i) in typePieLabels" :key="'tlbl'+i" :x="slice.x" :y="slice.y" text-anchor="middle" font-size="11" fill="#fff" font-weight="bold" pointer-events="none">{{ slice.count }}</text>
                    <text x="100" y="95" text-anchor="middle" font-size="15" fill="#303133" font-weight="bold">{{ issuesList.length }} 条</text>
                    <text x="100" y="114" text-anchor="middle" font-size="11" fill="#909399">类型分布</text>
                    <text v-if="typeFilter" x="100" y="128" text-anchor="middle" font-size="9" fill="#409eff" style="cursor:pointer" @click="typeFilter=''">✕ 清除</text>
                  </svg>
                  <div class="mini-legend"><div v-for="s in typePieData" :key="s.label" class="legend-row clickable" @click="filterTableByType(s.key)"><span class="legend-dot" :style="{ background: s.color }"></span><span style="font-size:12px" :style="{ fontWeight: typeFilter === s.key ? 'bold' : 'normal', color: typeFilter === s.key ? '#409eff' : '#606266' }">{{ s.label }} ({{ s.count }})</span></div></div>
                </div>
                <div style="display: flex; align-items: center; gap: 8px">
                  <svg viewBox="0 0 200 200" width="220" height="220">
                    <circle v-for="(slice, i) in wsPieData" :key="i" :cx="100" :cy="100" :r="80" fill="none" :stroke="slice.color" stroke-width="30" :stroke-dasharray="`${slice.pct * 502.65} ${(1 - slice.pct) * 502.65}`" :stroke-dashoffset="(wsPieOffset[i])" transform="rotate(-90 100 100)" style="cursor: pointer" @click="filterTableByWs(slice.name)" />
                    <text x="100" y="95" text-anchor="middle" font-size="15" fill="#303133" font-weight="bold">{{ wsTotal }} 条</text>
                    <text x="100" y="114" text-anchor="middle" font-size="11" fill="#909399">缺口分布</text>
                    <text v-if="wsFilter" x="100" y="128" text-anchor="middle" font-size="9" fill="#409eff" style="cursor:pointer" @click="wsFilter=''">✕ 清除</text>
                  </svg>
                  <div class="mini-legend"><div v-for="s in wsPieData" :key="s.name" class="legend-row clickable" @click="filterTableByWs(s.name)"><span class="legend-dot" :style="{ background: s.color }"></span><span style="font-size:12px" :style="{ fontWeight: wsFilter === s.name ? 'bold' : 'normal', color: wsFilter === s.name ? '#409eff' : '#606266' }">{{ s.name }} ({{ s.count }})</span></div></div>
                </div>
              </div>
            </el-card>
          </el-col>
        </el-row>
        <el-row :gutter="16" style="margin-bottom: 16px">
          <el-col :span="24">
            <el-card header="排班合理度趋势">
              <div v-if="rationalityData.length" ref="rationalityChartRef" class="area-chart-wrap" style="width: 100%; height: 420px"></div>
            </el-card>
          </el-col>
        </el-row>
        <el-table :data="filteredIssues" border stripe size="small" max-height="400">
          <el-table-column prop="workDate" label="日期" width="110" />
          <el-table-column label="时段" width="90"><template #default="{ row }">{{ row.timeSlot ? String(row.timeSlot).substring(0, 5) : '整周期' }}</template></el-table-column>
          <el-table-column prop="workstationName" label="工作站" width="120"><template #default="{ row }">{{ row.workstationName || '--' }}</template></el-table-column>
          <el-table-column label="类型" width="110"><template #default="{ row }"><el-tag v-if="row.issueType === 'STAFFING_GAP'" type="danger" size="small">岗位缺口</el-tag><el-tag v-else-if="row.issueType === 'SKILL_MISMATCH'" type="warning" size="small">技能不匹配</el-tag><el-tag v-else-if="row.issueType === 'OVERTIME'" type="info" size="small">工时超限</el-tag><el-tag v-else-if="row.issueType === 'CONSECUTIVE_WORK'" type="info" size="small">连续工作超限</el-tag><el-tag v-else-if="row.issueType === 'BREAK_BORROW_INEXPERIENCED'" type="info" size="small">不熟练顶岗</el-tag><el-tag v-else-if="row.issueType === 'MIN_DAILY_HOURS'" type="warning" size="small">每日工时不足</el-tag><el-tag v-else size="small">{{ row.issueType }}</el-tag></template></el-table-column>
          <el-table-column prop="severity" label="严重度" width="80"><template #default="{ row }"><el-tag :type="row.severity === 'ERROR' ? 'danger' : (row.severity === 'INFO' ? 'info' : 'warning')" size="small">{{ row.severity === 'ERROR' ? '错误' : (row.severity === 'INFO' ? '提示' : '警告') }}</el-tag></template></el-table-column>
          <el-table-column prop="description" label="说明" min-width="280" show-overflow-tooltip />
        </el-table>
      </div>
    </el-card>

    <!-- 拖动工作段时的吸附幽灵块 -->
    <div
      v-if="dragGhost.visible"
      class="drag-ghost"
      :style="{ left: dragGhost.x + 'px', top: dragGhost.y + 'px', width: dragGhost.width + 'px', height: dragGhost.height + 'px' }"
    >{{ dragGhost.text }}</div>

    <!-- 双击色块：切换该半小时 休息/上班 -->
    <el-dialog v-model="slotStatusDialog.visible" title="切换时段状态" width="420px">
      <div class="ds-info">
        <div><span class="ds-label">员工：</span>{{ slotStatusDialog.employeeName }}（{{ slotStatusDialog.employeeNo }}）</div>
        <div><span class="ds-label">日期：</span>{{ slotStatusDialog.date }}　<span class="ds-label">时段：</span>{{ slotStatusDialog.timeSlot }} - {{ slotStatusDialog.timeSlotEnd }}</div>
      </div>
      <div style="margin: 10px 0 4px; font-size: 12px; color: #909399">
        当前状态：<el-tag size="small" :type="slotStatusDialog.currentIsBreak ? 'warning' : 'success'">{{ slotStatusDialog.currentIsBreak ? '休息' : '上班' }}</el-tag>
      </div>
      <el-radio-group v-model="slotStatusDialog.action" style="margin: 10px 0; width: 100%">
        <el-radio value="work" style="margin-right: 24px">该半小时上班</el-radio>
        <el-radio value="rest">该半小时休息</el-radio>
      </el-radio-group>
      <template #footer>
        <el-button @click="slotStatusDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="slotStatusDialog.saving" @click="submitSlotStatus">确定</el-button>
      </template>
    </el-dialog>

    <!-- P3 空位加人/范围换人：日明细点击空格子补人；选中已有安排后可换人 -->
    <el-dialog v-model="addSlotDialog.visible" :title="addSlotDialog.mode === 'replace' ? '换人' : '添加人员'" width="460px" destroy-on-close>
      <div class="ds-info">
        <div><span class="ds-label">日期：</span>{{ addSlotDialog.workDate }}　<span class="ds-label">工作站：</span>{{ addSlotDialog.wsName }}</div>
        <div><span class="ds-label">时段：</span>{{ addSlotDialog.rangeText }}</div>
      </div>
      <el-alert v-if="addSlotDialog.mode === 'replace'" type="warning" :closable="false" style="margin-top: 10px" title="将移除所选范围内现有人员的时段，替换为所选员工" />
      <el-select
        v-model="addSlotDialog.employeeId"
        placeholder="请选择员工"
        filterable
        style="width: 100%; margin-top: 10px"
        :loading="addSlotDialog.loading"
        @change="onAddSlotCandidateChange"
      >
        <el-option v-for="c in addSlotDialog.candidates" :key="c.employeeId" :value="c.employeeId" :label="c.name + '（' + c.employeeNo + '）'">
          <span>{{ c.name }}（{{ c.employeeNo }}）</span>
          <span style="float: right; color: #909399; font-size: 12px">{{ c.department }} · {{ c.skillScore }}分</span>
        </el-option>
      </el-select>
      <div v-if="addSlotDialog.selected" style="margin-top: 10px">
        <el-tag v-if="addSlotDialog.selected.isRestDay === 1" type="info" size="small" style="margin-right: 6px">当天休息 · 将自动转上班</el-tag>
        <el-tag v-if="addSlotDialog.selected.isParttime === 1" type="success" size="small" style="margin-right: 6px">兼职</el-tag>
        <el-tag size="small" style="margin-right: 6px">技能 {{ addSlotDialog.selected.skillScore }} 分</el-tag>
      </div>
      <div v-if="addSlotDialog.mode === 'add' && addSlotDialog.existingCount > 0" style="margin-top: 10px; font-size: 12px; color: #e6a23c">
        所选范围现有 {{ addSlotDialog.existingCount }} 人次在岗，新员工将与其<b>并存</b>（不会替换现有人员）。
      </div>
      <div style="margin-top: 10px; font-size: 12px; color: #909399">
        仅添加所选时段（不挂班次模板）{{ currentPlan && currentPlan.status === 'PUBLISHED' ? '；已发布计划添加后会通知该员工' : '' }}。候选已按技能、兼职岗位限制、当天请假与已排班过滤。
      </div>
      <template #footer>
        <el-button @click="addSlotDialog.visible = false">取消</el-button>
        <el-button type="primary" :loading="addSlotDialog.saving" :disabled="!addSlotDialog.employeeId" @click="submitAddSlot">确定添加</el-button>
      </template>
    </el-dialog>

    <!-- P0 交互：调整撤销条（5 秒窗口，误操作可回退） -->
    <transition name="el-fade-in">
      <div v-if="undoBar.visible" class="undo-bar">
        <span>{{ undoBar.text }}</span>
        <el-button size="small" type="primary" link @click="handleUndo">撤销</el-button>
      </div>
    </transition>
  </div>
</template>

<script setup>
import { onMounted, onBeforeUnmount, ref, reactive, computed, nextTick, watch } from 'vue'
import * as echarts from 'echarts'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getMonthView, getWeekView, getDailyView, getScheduleIssues, getScheduleRationality, getSchedules, setSlotStatus, moveScheduleSegment, getAddSlotCandidates, addScheduleSlot, removeScheduleSlot, replaceScheduleSlot, moveScheduleRange, clearScheduleRange } from '../api/schedules'
import { getPreferenceMatrix } from '../api/preferences'
import { getWorkstations } from '../api/workstations'

const route = useRoute()
const planId = ref(route.query.planId || '')
const loading = ref(false)
const errorMsg = ref('')
const viewMode = ref(route.query.mode || 'week')
const plans = ref([])
const plansLoading = ref(false)
const plansTableRef = ref(null)
// 当前选中的排班方案（用于日期范围限制与日明细默认日期）
const currentPlan = ref(null)
const weekRows = ref([])
const weekDays = ref([])
const weekStart = ref('')
const monthRows = ref([])
const weeks = ref([])
const selectedDate = ref('')
const dailyRows = ref([])
// P2 交互：偏好匹配角标（与店长历史偏好一致的色块显示绿点）
const prefMap = ref(new Map()) // key: employeeNo|workstationCode -> freq
let prefLoaded = false

// 审查修复（P2）：偏好矩阵以 workstationCode 为键，而日明细行只有 workstationName，
// 此前键口径不一致导致绿点永不显示；补 code↔name 映射后统一用 code 查询
const wsNameToCode = ref(new Map())

async function loadPreferenceMap() {
  if (prefLoaded) return
  prefLoaded = true
  try {
    const items = await getPreferenceMatrix()
    const map = new Map()
    for (const it of items || []) {
      if (it.freq >= 3) map.set(it.employeeNo + '|' + it.workstationCode, it.freq)
    }
    prefMap.value = map
  } catch (e) {
    // 偏好矩阵加载失败不影响排班查看（角标静默缺失）
  }
  try {
    const ws = await getWorkstations()
    wsNameToCode.value = new Map((ws || []).map(w => [w.name, w.code]))
  } catch (e) {
    /* 名称映射失败时角标静默缺失 */
  }
}

function prefMatch(emp, ws) {
  const code = wsNameToCode.value.get(emp.workstationName || ws) || emp.workstationName || ws
  return prefMap.value.has((emp.employeeNo || '') + '|' + code)
}
const dailyIssues = ref([])
const dailyLoading = ref(false)
const dayDate = ref('')
const issuesList = ref([])
const issuesLoading = ref(false)
const rationalityList = ref([])
const rationalityChartRef = ref(null)
let rationalityChart = null
// 周视图：低技能岗位兼职替补需求（岗位×日期 → 是否有缺口）
const weekPartTimeNeeds = ref([])
const weekPartTimeMap = ref({})

function weekdayName(i) { return ['周一','周二','周三','周四','周五','周六','周日'][i] }
function fmt(t) { return t ? String(t).substring(0, 5) : '--' }
function getWeekDay(row, date) { return row.days?.find(d => d.workDate === date) }

// 周视图：全职在前、兼职在后（同组按工号），供分隔行渲染
const sortedWeekRows = computed(() => {
  const list = [...(weekRows.value || [])]
  list.sort((a, b) => (Number(a.isParttime) - Number(b.isParttime)) || String(a.employeeNo || '').localeCompare(String(b.employeeNo || '')))
  return list
})
// 第一个兼职员工所在下标：在其前插入「兼职员工」分隔行
const firstPartTimeIndex = computed(() => sortedWeekRows.value.findIndex(r => Number(r.isParttime) === 1))

// 时间轴：13:00 为原点，每 30 分钟一段，共 34 段（13:00~次日 05:30；后端时段左闭右开，06:00 下班的班次止于 05:30）
const SLOT_COUNT = 34
const slots = computed(() => Array.from({ length: SLOT_COUNT }, (_, i) => {
  const min = 13 * 60 + i * 30
  const isNext = min >= 24 * 60
  const h = Math.floor((min % (24 * 60)) / 60)
  const m = min % 60
  const key = `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}`
  return { key, display: isNext ? key + '+1' : key, isNextDay: isNext }
}))
// P3-11: 缺口岗位也显示
const dailyWorkstations = computed(() => {
  const set = new Set()
  dailyRows.value.forEach(r => {
    if (r.workstationName) set.add(r.workstationName)
  })
  const currentDate = selectedDate.value || dayDate.value
  const nextDay = addDays(currentDate, 1)
  dailyIssues.value
    .filter(i => i.issueType === 'STAFFING_GAP' && (i.workDate === currentDate || i.workDate === nextDay))
    .forEach(i => {
      if (i.workstationName) set.add(i.workstationName)
    })
  return Array.from(set)
})
function isHour(s) { return s.key.endsWith(':00') }
// 日期字符串加天数（返回 yyyy-MM-dd；与 DailyScheduleView/MonthScheduleView 工具口径一致）
function addDays(dateStr, days) {
  if (!dateStr) return ''
  const d = new Date(dateStr + 'T00:00:00')
  d.setDate(d.getDate() + days)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
// 单元格内员工：全职在前、兼职在后，兼职色块用绿色 + 虚线间隔与全职隔开
function dailyCellUsers(ws, slot) {
  return dailyRows.value
    .filter(r => r.workstationName === ws && String(r.timeSlot).substring(0,5) === slot.key)
    .sort((a, b) => (Number(a.isParttime ?? 0) - Number(b.isParttime ?? 0)) || (Number(a.employeeId) - Number(b.employeeId)))
}
// 是否为该单元格第一个兼职色块（且其前有全职色块）→ 显示虚线间隔
function isFirstPartTimeChip(emp, ws, slot) {
  const users = dailyCellUsers(ws, slot)
  const idx = users.findIndex(u => u.employeeId === emp.employeeId)
  const firstPt = users.findIndex(u => Number(u.isParttime) === 1)
  return firstPt === idx && users.some(u => Number(u.isParttime) === 0)
}
// P3-12: 缺口按日期过滤；次日格（00:00-05:30）的缺口归属次日日历日
// （审查修复 M10：与 DailyScheduleView/MonthScheduleView 的 isNextDay+1 口径统一，否则次日缺口漏标）
function dailySlotIssues(ws, slot) {
  const currentDate = selectedDate.value || dayDate.value
  const date = slot.isNextDay ? addDays(currentDate, 1) : currentDate
  return dailyIssues.value.filter(i => i.issueType === 'STAFFING_GAP' && i.workDate === date && i.workstationName === ws && i.timeSlot && String(i.timeSlot).substring(0,5) === slot.key)
}
// 该员工在该时段是否处于班中休息（含跨午夜回绕）
function inBreak(row, slot) {
  // 兼职员工不显示休息标记（排班界面直接留空）
  if (Number(row.isParttime) === 1) return false
  if (!row.breakStartTime || !row.breakEndTime) return false
  const s = String(row.breakStartTime).substring(0, 5)
  const e = String(row.breakEndTime).substring(0, 5)
  const t = slot.key
  if (e > s) return t >= s && t < e
  return t >= s || t < e
}
// 休息时间段文本：20:00-20:30
function breakTimeText(row) {
  const s = String(row.breakStartTime || '').substring(0, 5)
  const e = String(row.breakEndTime || '').substring(0, 5)
  return (s && e) ? `${s}-${e}` : '--'
}
// 休息提示（仅显示休息时段）
function breakTip(row) {
  return `休息 ${breakTimeText(row)}`
}

// ============ 拖动移动工作段（时间平移 + 换工作站，吸附格子） ============
const dragMove = {
  active: false,
  moved: false,
  matrixEl: null,
  empId: 0,
  empName: '',
  shiftCode: '',
  fromWorkstationId: 0,
  segStartIdx: -1,
  segEndIdx: -1,
  targetSlotIdx: -1,
  targetWs: '',
  targetWorkstationId: 0,
  startX: 0,
  startY: 0,
  justDragged: false
}
const dragGhost = reactive({ visible: false, x: 0, y: 0, width: 0, height: 0, text: '' })
const snapTarget = reactive({ ws: '', slotIdx: -1 })

// 工作站名 → id（用于落点定位）
const wsIdByName = computed(() => {
  const m = {}
  dailyRows.value.forEach(r => {
    if (r.workstationName && r.workstationId != null) m[r.workstationName] = r.workstationId
  })
  return m
})

// 默认只移动被拖的那半个小时
function onChipMouseDown(e, emp, ws, slot) {
  if (e.button !== 0) return
  // 审查修复（P0）：批量模式下禁用色块拖拽/双击，避免框选时误触发移动工作段
  if (batchMode.value) return
  // 休息块不可拖动
  if (inBreak(emp, slot)) return
  const slotIdx = slots.value.findIndex(s => s.key === slot.key)
  dragMove.active = true
  dragMove.moved = false
  dragMove.matrixEl = e.target.closest('.matrix')
  dragMove.empId = emp.employeeId
  dragMove.empName = emp.employeeName || ''
  dragMove.shiftCode = emp.shiftCode || '班'
  dragMove.fromWorkstationId = emp.workstationId
  dragMove.segStartIdx = slotIdx
  dragMove.segEndIdx = slotIdx
  dragMove.targetSlotIdx = slotIdx
  dragMove.targetWs = ws
  dragMove.targetWorkstationId = emp.workstationId
  dragMove.startX = e.clientX
  dragMove.startY = e.clientY
  dragMove.justDragged = false
  dragGhost.visible = true
  updateDragGhost(e.clientX, e.clientY)
}

// 当前高亮的员工（当天全部工作色块），null 表示无高亮
const highlightedEmpId = ref(null)

// 单击（未拖动）→ 高亮该员工当天全部工作色块；再次单击同一员工取消高亮
function onChipClick(emp, slot) {
  if (dragMove.justDragged) {
    dragMove.justDragged = false
    return
  }
  if (Date.now() < suppressCellClickUntil) return
  highlightedEmpId.value = highlightedEmpId.value === emp.employeeId ? null : emp.employeeId
}

// 双击（未拖动）→ 切换该半小时 休息/上班（同时保持该员工高亮）
function onChipDblClick(emp, slot) {
  if (dragMove.justDragged) {
    dragMove.justDragged = false
    return
  }
  // 审查修复（P0）：批量模式下禁用双击休息弹窗
  if (batchMode.value) return
  // 双击的第二下单击已把高亮关掉，这里重新打开，保证对话框打开时高亮仍在
  highlightedEmpId.value = emp.employeeId
  openSlotStatusDialog(emp, slot)
}

function updateDragGhost(clientX, clientY) {
  const matrixEl = dragMove.matrixEl
  if (!matrixEl) return
  const rows = matrixEl.querySelectorAll('.m-row:not(.m-header)')
  let rowIdx = -1
  rows.forEach((row, i) => {
    const rect = row.getBoundingClientRect()
    if (clientY >= rect.top && clientY <= rect.bottom) rowIdx = i
  })
  if (rowIdx < 0 || rowIdx >= dailyWorkstations.value.length) {
    snapTarget.ws = ''
    snapTarget.slotIdx = -1
    return
  }
  const rowRect = rows[rowIdx].getBoundingClientRect()
  const cellsLeft = rowRect.left + 130 // 左侧工作站列宽
  const slotIdx = Math.max(0, Math.min(slots.value.length - 1, Math.floor((clientX - cellsLeft) / 72)))
  const targetWs = dailyWorkstations.value[rowIdx]

  dragMove.targetSlotIdx = slotIdx
  dragMove.targetWs = targetWs
  dragMove.targetWorkstationId = wsIdByName.value[targetWs] || 0
  snapTarget.ws = targetWs
  snapTarget.slotIdx = slotIdx

  dragGhost.x = cellsLeft + slotIdx * 72 + 1
  dragGhost.y = rowRect.top + 2
  dragGhost.width = 70 // 单格（半小时）
  dragGhost.height = Math.max(24, rowRect.height - 4)
  dragGhost.text = dragMove.empName + ' · ' + dragMove.shiftCode
}

function onDragMove(e) {
  if (!dragMove.active) return
  if (Math.abs(e.clientX - dragMove.startX) > 6 || Math.abs(e.clientY - dragMove.startY) > 6) dragMove.moved = true
  updateDragGhost(e.clientX, e.clientY)
}

async function onDragEnd() {
  if (!dragMove.active) return
  const wasMoved = dragMove.moved
  dragMove.active = false
  dragGhost.visible = false
  snapTarget.ws = ''
  snapTarget.slotIdx = -1

  if (!wasMoved) {
    return // 未拖动：交给 click 事件打开休息/上班对话框
  }

  if (!planId.value) {
    ElMessage.warning('请先在上方选择排班计划，再拖动调整')
    dragMove.justDragged = true
    return
  }

  const slotList = slots.value
  const fromSlotKey = slotList[dragMove.segStartIdx].key
  const toSlotKey = slotList[dragMove.targetSlotIdx].key
  const targetUnchanged =
    toSlotKey === fromSlotKey && dragMove.targetWorkstationId === dragMove.fromWorkstationId

  if (targetUnchanged || !dragMove.targetWorkstationId || !dragMove.targetWs) {
    dragMove.justDragged = true // 拖回原位/无效落点：视为取消
    return
  }

  try {
    const movePayload = {
      planId: planId.value,
      employeeId: dragMove.empId,
      workDate: selectedDate.value || dayDate.value,
      fromWorkstationId: dragMove.fromWorkstationId,
      fromTimeSlot: fromSlotKey,
      toTimeSlot: toSlotKey,
      toWorkstationId: dragMove.targetWorkstationId
    }
    await moveScheduleSegment(movePayload)
    ElMessage.success('已移动 ' + dragMove.empName + ' 的半小时 → ' + dragMove.targetWs + ' ' + toSlotKey)
    pushUndo({ type: 'move', text: '已移动 ' + dragMove.empName + ' → ' + dragMove.targetWs + ' ' + toSlotKey, payload: movePayload })
    await loadDay(selectedDate.value || dayDate.value)
  } catch (err) {
    // request 拦截器已提示错误
  }
}

// ===== P0 交互：调整撤销栈（误操作可回退，避免污染偏好学习信号） =====
const undoStack = ref([])
const undoBar = reactive({ visible: false, text: '' })
let undoBarTimer = null

function pushUndo(entry) {
  undoStack.value.push(entry)
  undoBar.text = entry.text
  undoBar.visible = true
  clearTimeout(undoBarTimer)
  undoBarTimer = setTimeout(() => { undoBar.visible = false }, 5000)
}

async function handleUndo() {
  const entry = undoStack.value.pop()
  if (!entry) return
  undoBar.visible = false
  try {
    if (entry.type === 'move') {
      // 反向移动：from/to 互换
      await moveScheduleSegment({
        planId: planId.value,
        employeeId: entry.payload.employeeId,
        workDate: entry.payload.workDate,
        fromWorkstationId: entry.payload.toWorkstationId,
        fromTimeSlot: entry.payload.toTimeSlot,
        toTimeSlot: entry.payload.fromTimeSlot,
        toWorkstationId: entry.payload.fromWorkstationId
      })
    } else if (entry.type === 'slot') {
      // 反向恢复 休息/上班
      await setSlotStatus(planId.value, {
        items: [{
          employeeId: entry.payload.employeeId,
          workDate: entry.payload.workDate,
          timeSlot: entry.payload.timeSlot,
          isRest: entry.payload.isRest === 1 ? 0 : 1
        }]
      })
    } else if (entry.type === 'slot-batch') {
      // 批量撤销：全部反向恢复为上班
      const reverseItems = (entry.payload.items || []).map(it => ({
        employeeId: it.employeeId,
        workDate: it.workDate,
        timeSlot: it.timeSlot,
        isRest: 0
      }))
      await setSlotStatus(planId.value, { items: reverseItems })
    } else if (entry.type === 'add-slot') {
      // 撤回空位加人：删除所加时段并按剩余时段重算
      await removeScheduleSlot(planId.value, {
        employeeId: entry.payload.employeeId,
        workDate: entry.payload.workDate,
        timeSlots: entry.payload.timeSlots,
        workstationId: entry.payload.workstationId
      })
    } else if (entry.type === 'move-range') {
      // 撤回范围平移：反向平移
      await moveScheduleRange(planId.value, entry.payload)
    } else if (entry.type === 'range-rest') {
      // 撤回休息切换：反向恢复
      await setSlotStatus(planId.value, { items: entry.payload.items })
    } else if (entry.type === 'cancel-range') {
      // 撤回取消排班：按员工逐个恢复原时段；非连续时段按连续段拆分，
      // 否则后端连续性校验会拒绝整批恢复（审查修复 P2-13）
      for (const r of entry.payload.restores || []) {
        const idxs = [...r.timeSlots].map(k => slotIndex(k)).filter(i => i >= 0).sort((a, b) => a - b)
        const runs = []
        let cur = []
        for (const i of idxs) {
          if (cur.length && i - cur[cur.length - 1] !== 1) { runs.push(cur); cur = [] }
          cur.push(i)
        }
        if (cur.length) runs.push(cur)
        for (const run of runs) {
          await addScheduleSlot(planId.value, {
            employeeId: r.employeeId,
            workDate: entry.payload.workDate,
            timeSlots: run.map(i => slots.value[i].key),
            workstationId: r.workstationId
          })
        }
      }
    }
    ElMessage.success('已撤销')
    await loadDay(selectedDate.value || dayDate.value)
  } catch {
    // 撤销失败：拦截器已提示，保留后续撤销机会
  }
}

// Ctrl+Z / Cmd+Z 撤销最近一次调整（输入框内不拦截，保留原生文本撤销）
function onUndoKeydown(e) {
  if (!(e.ctrlKey || e.metaKey) || e.shiftKey || e.altKey) return
  if (e.key !== 'z' && e.key !== 'Z') return
  const tag = e.target?.tagName || ''
  if (tag === 'INPUT' || tag === 'TEXTAREA' || e.target?.isContentEditable) return
  e.preventDefault()
  handleUndo()
}

// ===== P2 交互：批量模式（框选多格 → 批量改为休息） =====
const batchMode = ref(false)
const batchSelect = reactive({ active: false, startWsIdx: -1, startSi: -1, endWsIdx: -1, endSi: -1 })

function clearBatchSelect() {
  batchSelect.active = false
  batchSelect.startWsIdx = -1
  batchSelect.startSi = -1
  batchSelect.endWsIdx = -1
  batchSelect.endSi = -1
}

function onBatchMouseDown(e) {
  if (!batchMode.value) return
  const cell = batchCellFromEvent(e)
  if (!cell) return
  e.preventDefault()
  batchSelect.active = true
  batchSelect.startWsIdx = cell.wsIdx
  batchSelect.startSi = cell.si
  batchSelect.endWsIdx = cell.wsIdx
  batchSelect.endSi = cell.si
}

function onBatchMouseUp(e) {
  if (!batchMode.value || !batchSelect.active) return
  const cell = batchCellFromEvent(e)
  if (cell) {
    batchSelect.endWsIdx = cell.wsIdx
    batchSelect.endSi = cell.si
  }
}

function batchCellFromEvent(e) {
  const col = e.target?.closest?.('.m-slot-col')
  const row = e.target?.closest?.('.m-row')
  if (!col || !row) return null
  const wsIdx = dailyWorkstations.value.indexOf(row.querySelector('.m-ws-col')?.textContent?.replace('兼', '').trim())
  const si = slots.value.findIndex(s => s.key === (col.getAttribute('data-slot') || ''))
  return wsIdx >= 0 && si >= 0 ? { wsIdx, si } : null
}

function batchSelected(ws, si) {
  if (!batchSelect.active) return false
  const wsIdx = dailyWorkstations.value.indexOf(ws)
  const minWs = Math.min(batchSelect.startWsIdx, batchSelect.endWsIdx)
  const maxWs = Math.max(batchSelect.startWsIdx, batchSelect.endWsIdx)
  const minSi = Math.min(batchSelect.startSi, batchSelect.endSi)
  const maxSi = Math.max(batchSelect.startSi, batchSelect.endSi)
  return wsIdx >= minWs && wsIdx <= maxWs && si >= minSi && si <= maxSi
}

const batchTargetCount = computed(() => {
  if (!batchSelect.active) return 0
  let count = 0
  dailyWorkstations.value.forEach((ws, wsIdx) => {
    slots.value.forEach((slot, si) => {
      if (!batchSelected(ws, si)) return
      count += dailyCellUsers(ws, slot).filter(u => !inBreak(u, slot) && u.employeeId != null).length
    })
  })
  return count
})

async function commitBatchRest() {
  const date = selectedDate.value || dayDate.value
  if (!planId.value || !date) {
    ElMessage.warning('请先选择排班计划与日期')
    return
  }
  const items = []
  dailyWorkstations.value.forEach((ws, wsIdx) => {
    slots.value.forEach((slot, si) => {
      if (!batchSelected(ws, si)) return
      for (const u of dailyCellUsers(ws, slot)) {
        if (inBreak(u, slot) || u.employeeId == null) continue
        items.push({ employeeId: u.employeeId, workDate: date, timeSlot: slot.key + ':00', isRest: 1 })
      }
    })
  })
  if (items.length === 0) {
    ElMessage.info('选中区域内没有可改为休息的排班格')
    return
  }

  try {
    await setSlotStatus(planId.value, { items })
    ElMessage.success('已批量改为休息 ' + items.length + ' 格')
    pushUndo({
      type: 'slot-batch',
      text: '批量改为休息 ' + items.length + ' 格',
      payload: { items }
    })
    clearBatchSelect()
    await loadDay(date)
  } catch {
    // 拦截器已提示
  }
}

// ===== 点击色块：切换该半小时 休息/上班 =====  //
const slotStatusDialog = reactive({
  visible: false,
  employeeId: null,
  employeeName: '',
  employeeNo: '',
  date: '',
  timeSlot: '',
  timeSlotEnd: '',
  currentIsBreak: false,
  action: 'work', // work | rest
  saving: false
})

// 半小时 +30 分钟（跨午夜按 24 小时回绕）
function slotEnd(time) {
  const parts = String(time).split(':').map(Number)
  const total = (parts[0] * 60 + (parts[1] || 0) + 30) % 1440
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`
}

// 打开弹窗：默认动作与当前状态相反（休息→上班，上班→休息）
function openSlotStatusDialog(emp, slot) {
  const date = selectedDate.value || dayDate.value
  if (!date) return
  const isBreak = inBreak(emp, slot)
  slotStatusDialog.employeeId = emp.employeeId
  slotStatusDialog.employeeName = emp.employeeName || ''
  slotStatusDialog.employeeNo = emp.employeeNo || ''
  slotStatusDialog.date = date
  slotStatusDialog.timeSlot = slot.key
  slotStatusDialog.timeSlotEnd = slotEnd(slot.key)
  slotStatusDialog.currentIsBreak = isBreak
  slotStatusDialog.action = isBreak ? 'work' : 'rest'
  slotStatusDialog.saving = false
  slotStatusDialog.visible = true
}

async function submitSlotStatus() {
  const d = slotStatusDialog
  const isRest = d.action === 'rest'
  if (!planId.value || !d.employeeId || !d.date || !d.timeSlot) return
  d.saving = true
  try {
    await setSlotStatus(planId.value, {
      items: [{
        employeeId: d.employeeId,
        workDate: d.date,
        timeSlot: d.timeSlot + ':00',
        isRest: isRest ? 1 : 0
      }]
    })
    ElMessage.success(isRest
      ? `已改为休息（${d.timeSlot} - ${d.timeSlotEnd}）`
      : `已恢复上班（${d.timeSlot} - ${d.timeSlotEnd}）`)
    pushUndo({
      type: 'slot',
      text: (isRest ? '已改为休息 ' : '已恢复上班 ') + d.employeeName + ' ' + d.timeSlot,
      payload: { employeeId: d.employeeId, workDate: d.date, timeSlot: d.timeSlot + ':00', isRest: isRest ? 1 : 0 }
    })
    d.visible = false
    // 刷新日明细与月视图（休息计数）
    const date = selectedDate.value || dayDate.value
    await loadDay(date)
    try {
      monthRows.value = await getMonthView(planId.value)
      if (viewMode.value === 'month') buildCalendar()
    } catch {}
  } catch {
    // 失败提示已由 request 拦截器统一弹出
  } finally {
    d.saving = false
  }
}
// 低技能岗位缺口：绿色标记 + 兼职建议
function lowSkillGapIssues(ws, slot) {
  return dailySlotIssues(ws, slot).filter(i => i.isLowSkill)
}
// 判断工作站是否低技能岗位
function wsLowSkill(ws) {
  return dailyIssues.value.some(i => i.workstationName === ws && i.isLowSkill)
}
// 缺口提示：低技能岗位显示兼职建议
function gapTooltip(ws, slot) {
  const low = lowSkillGapIssues(ws, slot)
  if (low.length > 0) {
    return '该岗位技术含量低，建议寻找兼职人员临时填补'
  }
  return '岗位缺口'
}
function cellClass(ws, slot) {
  return [
    dailyCellUsers(ws,slot).length > 0 && 'has-employee',
    slot.isNextDay && 'next-day',
    dailySlotIssues(ws,slot).length > 0 && (lowSkillGapIssues(ws, slot).length > 0 ? 'has-gap-low-skill' : 'has-gap')
  ].filter(Boolean).join(' ')
}

async function loadAll() {
  loading.value = true; errorMsg.value = ''
  try {
    if (viewMode.value === 'issues') await loadIssues()
    else if (viewMode.value === 'week') await loadWeek()
    else if (viewMode.value === 'whole') await loadWhole()
    else if (viewMode.value === 'month') await loadMonth()
    else if (viewMode.value === 'day') await loadDay()
  } catch (e) { errorMsg.value = e.message }
  finally { loading.value = false }
}

// 回退：从缺口描述计算每日合理度（缺N人/需求M）
function computeRationalityFromIssues(issues) {
  const demandByDate = {}
  const missingByDate = {}
  ;(issues || []).filter(i => i.issueType === 'STAFFING_GAP' && i.workDate).forEach(i => {
    const d = i.workDate
    const desc = i.description || ''
    const mDemand = desc.match(/需求\s*(\d+)/)
    const mMissing = desc.match(/缺\s*(\d+)\s*人/)
    const demand = mDemand ? parseInt(mDemand[1]) : 0
    const missing = mMissing ? parseInt(mMissing[1]) : 0
    demandByDate[d] = (demandByDate[d] || 0) + demand
    missingByDate[d] = (missingByDate[d] || 0) + missing
  })
  return Object.keys(demandByDate).sort().map(d => {
    const demand = demandByDate[d] || 1
    const missing = missingByDate[d] || 0
    return { date: d, pct: Math.max(0, Math.min(100, Math.round((1 - missing / demand) * 100))) }
  })
}

async function loadIssues() {
  if (!planId.value) return
  issuesLoading.value = true
  try {
    const issues = (await getScheduleIssues(planId.value)) || []
    // 不展示“无人顶岗”类问题（休息无人顶岗提示整体下线）
    issuesList.value = issues.filter(i => i.issueType !== 'BREAK_UNCOVERED')
    // 优先使用后端 /rationality 端点；404/失败时回退从缺口描述解析（兼容未升级的后端）
    try {
      const rationality = await getScheduleRationality(planId.value)
      rationalityList.value = rationality || []
    } catch (e) {
      rationalityList.value = computeRationalityFromIssues(issues)
    }
  } catch (e) {
    /* 拦截器已提示 */
  } finally { issuesLoading.value = false }
}

function getToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 当前选中排班方案的日期范围（YYYY-MM-DD）
function planRange() {
  if (!currentPlan.value?.startDate || !currentPlan.value?.endDate) return null
  return { start: currentPlan.value.startDate, end: currentPlan.value.endDate }
}

// 日期选择器限制：只能选该排班方案周期内的日期
function disabledDate(date) {
  const range = planRange()
  if (!range) return false
  const start = new Date(range.start + 'T00:00:00')
  const end = new Date(range.end + 'T00:00:00')
  return date < start || date > end
}

// 审查修复（P1）：请求序号防竞态——快速切换日期/计划时旧响应不再覆盖新数据
let weekLoadSeq = 0
let wholeLoadSeq = 0
let dayLoadSeq = 0

async function loadWeek() {
  if (!planId.value) { errorMsg.value = '请输入计划ID'; return }
  if (!weekStart.value) { weekStart.value = currentPlan.value?.startDate || getToday() }
  const seq = ++weekLoadSeq
  const res = await getWeekView(planId.value, weekStart.value)
  if (seq !== weekLoadSeq) return
  weekRows.value = res
  // 并集所有行的日期，避免只取第一行而遗漏其他员工的班次日期
  const dateSet = new Set()
  weekRows.value.forEach(r => {
    (r.days || []).forEach(d => {
      if (d && d.workDate) dateSet.add(d.workDate)
    })
  })
  weekDays.value = Array.from(dateSet).sort().map(date => {
    const dow = new Date(date + 'T00:00:00').getDay()
    return { date, weekday: dow === 0 ? 6 : dow - 1 }
  })

  // 加载低技能岗位缺口 → 生成兼职替补需求色块
  try {
    const iss = await getScheduleIssues(planId.value)
    if (seq !== weekLoadSeq) return
    const lowSkillGaps = (iss || []).filter(i => i.issueType === 'STAFFING_GAP' && i.isLowSkill && i.workDate && i.workstationName)
    const map = {}
    const wsSet = new Set()
    lowSkillGaps.forEach(i => {
      map[`${i.workstationName}|${i.workDate}`] = true
      wsSet.add(i.workstationName)
    })
    weekPartTimeMap.value = map
    weekPartTimeNeeds.value = Array.from(wsSet).map(ws => ({ workstationName: ws }))
  } catch (e) {
    weekPartTimeNeeds.value = []
    weekPartTimeMap.value = {}
  }
}

// 该低技能岗位在该日是否有兼职缺口
function hasPartTimeNeed(ws, date) {
  return !!weekPartTimeMap.value[`${ws}|${date}`]
}

async function loadMonth() { if (!planId.value) { errorMsg.value = '请输入计划ID'; return }; monthRows.value = await getMonthView(planId.value); buildCalendar() }

function fmtDate(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// 整月排班：拉取计划周期内所有周的数据合并，按"员工 × 整月日期"渲染（样式与周视图一致）
async function loadWhole() {
  if (!planId.value) { errorMsg.value = '请输入计划ID'; return }
  const plan = currentPlan.value
  if (!plan) return
  const seq = ++wholeLoadSeq

  // 周期拆成自然周（每 7 天一段）并行拉取，再按员工合并
  const weeks = []
  const end = new Date(plan.endDate + 'T00:00:00')
  for (let d = new Date(plan.startDate + 'T00:00:00'); d <= end; d.setDate(d.getDate() + 7)) {
    weeks.push(fmtDate(d))
  }
  const weekResults = await Promise.all(weeks.map(w => getWeekView(planId.value, w)))
  if (seq !== wholeLoadSeq) return

  const empMap = new Map()
  for (const week of weekResults) {
    for (const row of week || []) {
      let entry = empMap.get(row.employeeId)
      if (!entry) {
        entry = {
          employeeId: row.employeeId,
          employeeNo: row.employeeNo,
          employeeName: row.employeeName,
          department: row.department,
          isParttime: row.isParttime,
          days: []
        }
        empMap.set(row.employeeId, entry)
      }
      entry.days = entry.days.concat(row.days || [])
    }
  }
  weekRows.value = Array.from(empMap.values())

  // 整月日期轴：计划周期内的每一天
  const days = []
  for (let d = new Date(plan.startDate + 'T00:00:00'); d <= end; d.setDate(d.getDate() + 1)) {
    const dow = d.getDay()
    days.push({ date: fmtDate(d), weekday: dow === 0 ? 6 : dow - 1 })
  }
  weekDays.value = days
}

async function loadDay(date) {
  if (!planId.value) return
  let d = date || dayDate.value
  const range = planRange()
  // 自动筛选当前方案：日期缺省或不在方案周期内时，回落到今天（不在周期内则取方案开始日）
  if (!d || (range && (d < range.start || d > range.end))) {
    if (date) return
    const today = getToday()
    d = range ? (today >= range.start && today <= range.end ? today : range.start) : ''
    dayDate.value = d
    if (!d) return
  }
  dailyLoading.value = true
  const seq = ++dayLoadSeq
  try {
    // 同时拉取月视图数据：用于矩阵下方的「休息员工」区
    const [res, iss, month] = await Promise.all([
      getDailyView(planId.value, d),
      getScheduleIssues(planId.value),
      getMonthView(planId.value)
    ])
    if (seq !== dayLoadSeq) return
    dailyRows.value = res || []
    dailyIssues.value = iss || []
    if (Array.isArray(month)) monthRows.value = month
  } catch (e) {
    /* 拦截器已提示 */
  } finally { dailyLoading.value = false }
}

async function selectDate(date) { selectedDate.value = date; highlightedEmpId.value = null; await loadDay(date) }

// ===== P3 空位加人：日明细空格子点击/滑动多选 → 选择员工补 30 分钟上班段 =====
const addSlotDialog = reactive({
  visible: false,
  loading: false,
  saving: false,
  mode: 'add',      // add=空位加人；replace=范围换人（移除范围内现有安排）
  existingCount: 0, // 所选范围现有在岗人次（add 模式下提示并存）
  wsName: '',
  wsId: null,
  timeSlots: [],   // 所选连续时段（'HH:mm' 数组）
  rangeText: '',
  workDate: '',
  employeeId: null,
  candidates: []
})

// 滑动选择状态（时间轴同一行内横向滑动）
const rangeSelect = reactive({
  active: false,
  moved: false,
  ws: '',
  startKey: '',
  endKey: ''
})
let suppressCellClickUntil = 0

// 工作站名称 → id 映射（来源：日明细行 + 缺口问题行）
const wsNameToId = computed(() => {
  const map = new Map()
  dailyRows.value.forEach(r => { if (r.workstationName && r.workstationId) map.set(r.workstationName, r.workstationId) })
  dailyIssues.value.forEach(i => { if (i.workstationName && i.workstationId) map.set(i.workstationName, i.workstationId) })
  return map
})

const addSlotSelected = computed(() =>
  addSlotDialog.candidates.find(c => c.employeeId === addSlotDialog.employeeId) || null)

function slotIndex(key) {
  return slots.value.findIndex(s => s.key === key)
}

function slotKeysBetween(startKey, endKey) {
  const a = slotIndex(startKey)
  const b = slotIndex(endKey)
  if (a < 0 || b < 0) return []
  const lo = Math.min(a, b)
  const hi = Math.max(a, b)
  return slots.value.slice(lo, hi + 1).map(s => s.key)
}

// 时段结束标签：该时段 + 30 分钟（时间轴最末 05:30+1 → 06:00+1）
function slotEndDisplay(key) {
  const idx = slotIndex(key)
  const next = idx >= 0 ? slots.value[idx + 1] : null
  return next ? next.display : (key === '05:30' ? '06:00+1' : key)
}

const rangeHint = computed(() => {
  if (!rangeSelect.active) return ''
  const keys = slotKeysBetween(rangeSelect.startKey, rangeSelect.endKey)
  if (!keys.length) return ''
  const start = slots.value[slotIndex(rangeSelect.startKey)]
  return `已选 ${keys.length} 段（${start.display} ~ ${slotEndDisplay(rangeSelect.endKey)}），松开鼠标确认`
})

function rangeSelected(ws, slot) {
  if (!rangeSelect.active || ws !== rangeSelect.ws) return false
  const a = slotIndex(rangeSelect.startKey)
  const b = slotIndex(rangeSelect.endKey)
  const i = slotIndex(slot.key)
  return i >= Math.min(a, b) && i <= Math.max(a, b)
}

// 滑动起点：任何格子（含有人格子）都可进入范围选择；chip 单击/双击行为保留
function onCellMouseDown(ws, slot, e) {
  if (e && e.button !== undefined && e.button !== 0) return
  // 审查修复（P2-14）：批量模式下的框选不与范围选择并存
  if (batchMode.value) return
  if (e) e.preventDefault()
  rangeSelect.active = true
  rangeSelect.moved = false
  rangeSelect.ws = ws
  rangeSelect.startKey = slot.key
  rangeSelect.endKey = slot.key
  document.addEventListener('mousemove', onRangeMouseMove)
  document.addEventListener('mouseup', onRangeMouseUp)
}

function cellAtPoint(e) {
  const el = document.elementFromPoint(e.clientX, e.clientY)
  const cell = el?.closest?.('.m-slot-col')
  if (!cell) return null
  const row = cell.closest('.m-row')
  const wsEl = row?.querySelector('.m-ws-col')
  return { ws: wsEl ? wsEl.innerText.replace('兼', '').trim() : '', key: cell.getAttribute('data-slot') }
}

function onRangeMouseMove(e) {
  if (!rangeSelect.active) return
  const hit = cellAtPoint(e)
  if (!hit || hit.ws !== rangeSelect.ws || !hit.key) return
  if (hit.key !== rangeSelect.endKey) rangeSelect.moved = true
  rangeSelect.endKey = hit.key
}

// ===== P3 范围选择工具条（对已有安排：换人 / 平移 / 休息切换） =====
const rangeSel = reactive({ visible: false, ws: '', startKey: '', endKey: '', restEmployeeId: null })

const rangeSelKeys = computed(() => rangeSel.visible ? slotKeysBetween(rangeSel.startKey, rangeSel.endKey) : [])
const rangeSelCount = computed(() => rangeSelKeys.value.length)
const rangeSelStart = computed(() => { const s = slots.value[slotIndex(rangeSel.startKey)]; return s ? s.display : '' })
const rangeSelEnd = computed(() => slotEndDisplay(rangeSel.endKey))

function rangeSelSelected(ws, slot) {
  if (!rangeSel.visible || ws !== rangeSel.ws) return false
  return rangeSelKeys.value.includes(slot.key)
}

// 范围内出现的员工（供休息切换下拉）
const rangeSelEmployees = computed(() => {
  const map = new Map()
  for (const k of rangeSelKeys.value) {
    const slot = slots.value[slotIndex(k)]
    if (!slot) continue
    for (const u of dailyCellUsers(rangeSel.ws, slot)) {
      if (!map.has(u.employeeId)) {
        map.set(u.employeeId, { id: u.employeeId, name: u.employeeName || '', no: u.employeeNo || '' })
      }
    }
  }
  return [...map.values()]
})

// 休息切换按钮文案：该员工在范围内的时段若已处于休息（班中休息窗口覆盖范围起点）→ 恢复上班
const rangeSelRestLabel = computed(() => {
  const empId = rangeSel.restEmployeeId
  if (!empId) return '改为休息'
  const keys = rangeSelKeys.value
  if (!keys.length) return '改为休息'
  const slot = slots.value[slotIndex(keys[0])]
  const u = slot ? dailyCellUsers(rangeSel.ws, slot).find(x => x.employeeId === empId) : null
  return u && inBreak(u, slot) ? '恢复上班' : '改为休息'
})

function rangeHasEmployees(ws, keys) {
  return keys.some(k => {
    const s = slots.value[slotIndex(k)]
    return s && dailyCellUsers(ws, s).length > 0
  })
}

function clearRangeSel() {
  rangeSel.visible = false
  rangeSel.restEmployeeId = null
}

function onRangeMouseUp() {
  document.removeEventListener('mousemove', onRangeMouseMove)
  document.removeEventListener('mouseup', onRangeMouseUp)
  if (!rangeSelect.active) return
  const ws = rangeSelect.ws
  const startKey = rangeSelect.startKey
  const endKey = rangeSelect.endKey
  const moved = rangeSelect.moved
  rangeSelect.active = false
  rangeSelect.moved = false
  if (moved) {
    // 滑动结束：吞掉随后的 click 事件
    suppressCellClickUntil = Date.now() + 350
    const keys = slotKeysBetween(startKey, endKey)
    const occupied = rangeHasEmployees(ws, keys)
    if (!occupied) {
      // 全空范围 → 直接弹添加人员
      openAddSlot(ws, keys)
    } else if (currentPlan.value?.status === 'PUBLISHED') {
      ElMessage.warning('已发布排班仅支持空位加人，不能修改已有安排')
    } else {
      // 有人的范围 → 显示工具条（换人/平移/休息）
      rangeSel.visible = true
      rangeSel.ws = ws
      rangeSel.startKey = startKey
      rangeSel.endKey = endKey
      rangeSel.restEmployeeId = rangeSelEmployees.value.length === 1 ? rangeSelEmployees.value[0].id : null
    }
  }
  // 未移动（纯点击）：交由 click 事件走单时段流程
}

// 换人：打开添加人员弹窗（替换模式）
function openReplaceDialog() {
  const keys = [...rangeSelKeys.value]
  if (!keys.length) return
  openAddSlot(rangeSel.ws, keys, 'replace')
}

// 加人：满员/有人时段也能再加人——新员工与现有人员并存（不替换）
function openAddDialog() {
  const keys = [...rangeSelKeys.value]
  if (!keys.length) return
  openAddSlot(rangeSel.ws, keys, 'add')
}

// 左移/右移 30 分钟
async function moveRangeBy(offset) {
  const keys = [...rangeSelKeys.value]
  if (!keys.length || !dayDate.value) return
  const wsId = wsNameToId.value.get(rangeSel.ws)
  if (!wsId) return
  // 审查修复（P1-1）：撤销条目存平移后的新时段，按新时段反平移（此前存旧时段必失败）
  const delta = offset > 0 ? 1 : -1
  const si = slotIndex(rangeSel.startKey) + delta
  const ei = slotIndex(rangeSel.endKey) + delta
  const shiftedKeys = (si >= 0 && ei < slots.value.length)
    ? slots.value.slice(Math.min(si, ei), Math.max(si, ei) + 1).map(s => s.key)
    : []
  try {
    await moveScheduleRange(planId.value, {
      workDate: dayDate.value,
      workstationId: wsId,
      timeSlots: keys,
      offsetMinutes: offset
    })
    ElMessage.success(offset < 0 ? '已左移 30 分钟' : '已右移 30 分钟')
    pushUndo({
      type: 'move-range',
      text: `范围平移 ${offset < 0 ? '左移' : '右移'} 30 分钟（${rangeSel.ws} ${rangeSelCount.value} 段）`,
      payload: { workDate: dayDate.value, workstationId: wsId, timeSlots: shiftedKeys.length ? shiftedKeys : keys, offsetMinutes: -offset }
    })
    // 平移选择范围并刷新（保持工具条可用，可连续点按）
    if (shiftedKeys.length) {
      rangeSel.startKey = shiftedKeys[0]
      rangeSel.endKey = shiftedKeys[shiftedKeys.length - 1]
    } else {
      clearRangeSel()
    }
    await loadDay(dayDate.value)
  } catch (e) {
    /* 拦截器已提示 */
  }
}

// 休息切换：只改所选员工的时段（班中休息为每天一个 30 分钟窗口，落在范围内首个该员工时段）
async function toggleRangeRest() {
  const empId = rangeSel.restEmployeeId
  const keys = [...rangeSelKeys.value]
  if (!empId || !keys.length || !dayDate.value) return
  const items = []
  for (const k of keys) {
    const slot = slots.value[slotIndex(k)]
    if (!slot) continue
    const u = dailyCellUsers(rangeSel.ws, slot).find(x => x.employeeId === empId)
    if (u) {
      items.push({ employeeId: empId, workDate: dayDate.value, timeSlot: k + ':00', isRest: inBreak(u, slot) ? 0 : 1 })
    }
  }
  if (items.length === 0) {
    ElMessage.warning('所选范围内该员工没有时段')
    return
  }
  const toRest = items[0].isRest === 1
  // 班中休息每天一个 30 分钟窗口：改为休息只标记范围内该员工首个时段；恢复上班则清除所有标记
  const submitItems = toRest ? [items[0]] : items
  try {
    await setSlotStatus(planId.value, { items: submitItems })
    ElMessage.success(toRest ? '已改为休息（30 分钟）' : '已恢复上班')
    pushUndo({
      type: 'range-rest',
      text: (toRest ? '已改为休息 ' : '已恢复上班 ') + (rangeSelEmployees.value.find(x => x.id === empId)?.name || ''),
      payload: { items: submitItems.map(it => ({ ...it, isRest: it.isRest === 1 ? 0 : 1 })) }
    })
    await loadDay(dayDate.value)
  } catch (e) {
    /* 拦截器已提示 */
  }
}

// 取消排班：选了员工只取消该员工在范围内的时段；未选则取消范围内所有人（需确认）
async function cancelRangeSchedule() {
  const keys = [...rangeSelKeys.value]
  if (!keys.length || !dayDate.value) return
  const wsId = wsNameToId.value.get(rangeSel.ws)
  if (!wsId) return

  const empId = rangeSel.restEmployeeId
  // 收集范围内的明细（用于撤销恢复与受影响人数统计）
  const byEmployee = new Map() // employeeId -> { name, timeSlots }
  for (const k of keys) {
    const slot = slots.value[slotIndex(k)]
    if (!slot) continue
    for (const u of dailyCellUsers(rangeSel.ws, slot)) {
      const entry = byEmployee.get(u.employeeId) || { name: u.employeeName || u.employeeNo, timeSlots: [] }
      entry.timeSlots.push(k)
      byEmployee.set(u.employeeId, entry)
    }
  }
  if (byEmployee.size === 0) {
    ElMessage.warning('所选范围没有排班记录')
    return
  }

  // 确认
  let confirmText = ''
  if (empId && byEmployee.has(empId)) {
    const e = byEmployee.get(empId)
    confirmText = `确定取消 ${e.name} 在所选范围的排班（${e.timeSlots.length} 段）吗？取消后这些时段直接下班。`
  } else {
    const people = [...byEmployee.values()].map(e => e.name).join('、')
    confirmText = `确定取消所选范围内全部排班吗？涉及 ${people}（共 ${byEmployee.size} 人），取消后这些时段直接下班。`
  }
  try {
    await ElMessageBox.confirm(confirmText, '取消排班', { type: 'warning', confirmButtonText: '取消排班', cancelButtonText: '再想想' })
  } catch {
    return
  }

  try {
    let restorePayload = []
    if (empId && byEmployee.has(empId)) {
      const e = byEmployee.get(empId)
      await removeScheduleSlot(planId.value, {
        employeeId: empId,
        workDate: dayDate.value,
        timeSlots: e.timeSlots,
        workstationId: wsId
      })
      restorePayload = [{ employeeId: empId, timeSlots: e.timeSlots, workstationId: wsId }]
      ElMessage.success(`已取消 ${e.name} 的排班（${e.timeSlots.length} 段）`)
    } else {
      await clearScheduleRange(planId.value, {
        workDate: dayDate.value,
        workstationId: wsId,
        timeSlots: keys
      })
      restorePayload = [...byEmployee.entries()].map(([id, e]) => ({ employeeId: id, timeSlots: e.timeSlots, workstationId: wsId }))
      ElMessage.success(`已取消范围内排班（${byEmployee.size} 人）`)
    }
    pushUndo({
      type: 'cancel-range',
      text: '已取消排班 ' + (empId ? byEmployee.get(empId)?.name : rangeSel.ws + ' ' + rangeSelCount.value + ' 段'),
      payload: { restores: restorePayload, workDate: dayDate.value }
    })
    clearRangeSel()
    await loadDay(dayDate.value)
  } catch (e) {
    /* 拦截器已提示 */
  }
}

// 点击格子：拖动结束后的 click 被忽略；空位打开添加人员弹窗；非空仅清除高亮
function onCellClick(ws, slot) {
  if (Date.now() < suppressCellClickUntil) return
  if (dailyCellUsers(ws, slot).length === 0) {
    openAddSlot(ws, [slot.key])
  } else {
    highlightedEmpId.value = null
  }
}

async function openAddSlot(ws, slotKeys, mode = 'add') {
  const wsId = wsNameToId.value.get(ws)
  if (!wsId || !dayDate.value || !Array.isArray(slotKeys) || slotKeys.length === 0) {
    ElMessage.warning('无法识别该工作站或日期，请刷新后重试')
    return
  }
  addSlotDialog.mode = mode
  addSlotDialog.wsName = ws
  addSlotDialog.wsId = wsId
  addSlotDialog.timeSlots = slotKeys
  const startSlot = slots.value[slotIndex(slotKeys[0])]
  addSlotDialog.rangeText = `${startSlot.display} ~ ${slotEndDisplay(slotKeys[slotKeys.length - 1])}（${slotKeys.length} 段）`
  // 所选范围现有在岗人次（add 模式下提示「并存」，replace 模式下提示「将被替换」）
  addSlotDialog.existingCount = slotKeys.reduce((n, k) => {
    const s = slots.value[slotIndex(k)]
    return n + (s ? dailyCellUsers(ws, s).length : 0)
  }, 0)
  addSlotDialog.workDate = dayDate.value
  addSlotDialog.employeeId = null
  addSlotDialog.candidates = []
  addSlotDialog.visible = true
  await loadAddSlotCandidates()
}

async function loadAddSlotCandidates() {
  addSlotDialog.loading = true
  try {
    const res = await getAddSlotCandidates(planId.value, {
      workDate: addSlotDialog.workDate,
      timeSlot: addSlotDialog.timeSlots[0],
      workstationId: addSlotDialog.wsId
    })
    addSlotDialog.candidates = Array.isArray(res) ? res : []
    if (addSlotDialog.candidates.length === 0) {
      ElMessage.info('没有符合条件的候选员工（需具备该岗位技能、当天未排班且无已批准请假）')
    }
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    addSlotDialog.loading = false
  }
}

function onAddSlotCandidateChange() {
  // 选中变化时无需额外动作（提示标签由 addSlotSelected 计算）
}

async function submitAddSlot() {
  if (!addSlotDialog.employeeId) return
  addSlotDialog.saving = true
  try {
    const payload = {
      employeeId: addSlotDialog.employeeId,
      workDate: addSlotDialog.workDate,
      timeSlots: addSlotDialog.timeSlots,
      workstationId: addSlotDialog.wsId
    }
    if (addSlotDialog.mode === 'replace') {
      await replaceScheduleSlot(planId.value, payload)
      ElMessage.success(`换人成功（${addSlotDialog.timeSlots.length} 段）`)
    } else {
      await addScheduleSlot(planId.value, payload)
      ElMessage.success(`添加成功（${addSlotDialog.timeSlots.length} 段）`)
      // 入撤销栈：支持撤销条 / Ctrl+Z（Cmd+Z）撤回
      pushUndo({
        type: 'add-slot',
        text: `已添加 ${addSlotSelected.value?.name || ''}（${addSlotDialog.wsName} ${addSlotDialog.rangeText}）`,
        payload: { ...payload, timeSlots: [...payload.timeSlots] }
      })
    }
    addSlotDialog.visible = false
    clearRangeSel()
    // 刷新日明细与缺口标记
    await loadDay(addSlotDialog.workDate)
  } catch (e) {
    /* 拦截器已提示 */
  } finally {
    addSlotDialog.saving = false
  }
}

function onModeChange() {
  selectedDate.value = ''
  dailyRows.value = []
  highlightedEmpId.value = null
  wsFilter.value = ''
  typeFilter.value = ''
  // 切到日明细时：默认日期限定在当前方案的周期内
  if (viewMode.value === 'day') {
    const range = planRange()
    if (!dayDate.value || (range && (dayDate.value < range.start || dayDate.value > range.end))) {
      const today = getToday()
      dayDate.value = range ? (today >= range.start && today <= range.end ? today : range.start) : ''
    }
  }
  loadAll()
}

function buildCalendar() {
  if (!monthRows.value.length) { weeks.value = []; return }
  // P3-10: 从所有行收集日期
  const daySet = new Set()
  monthRows.value.forEach(row => {
    (row.days || []).forEach(d => {
      if (d?.workDate) daySet.add(d.workDate)
    })
  })
  const days = Array.from(daySet).sort()
  if (days.length === 0) { weeks.value = []; return }
  const stats = {}; days.forEach(d => { stats[d] = { w: 0, r: 0, pt: 0, s: new Set() } })
  monthRows.value.forEach(row => row.days.forEach(d => {
    if (d.isRestDay === 1) {
      // 兼职员工空闲日不算休息（排班界面直接留空）
      if (Number(row.isParttime) !== 1) stats[d.workDate].r++
    } else {
      stats[d.workDate].w++
      // 兼职上班人数单独统计，月历里与全职分开显示
      if (Number(row.isParttime) === 1) stats[d.workDate].pt++
      if (d.shiftCode) stats[d.workDate].s.add(d.shiftCode)
    }
  }))
  const fd = new Date(days[0] + 'T00:00:00'), ld = new Date(days[days.length - 1] + 'T00:00:00')
  const cs = new Date(fd); cs.setDate(fd.getDate() - ((fd.getDay() + 6) % 7))
  const ce = new Date(ld); ce.setDate(ld.getDate() + (6 - (ld.getDay() + 6) % 7))
  const ws = []; const c = new Date(cs)
  while (c <= ce) {
    const w = []
    for (let i = 0; i < 7; i++) {
      const k = `${c.getFullYear()}-${String(c.getMonth()+1).padStart(2,'0')}-${String(c.getDate()).padStart(2,'0')}`
      if (c >= fd && c <= ld && stats[k]) { const s = stats[k]; w.push({ date: k, dayNum: c.getDate(), workCount: s.w, partTimeWorkCount: s.pt, restCount: s.r, shiftSummary: Array.from(s.s).slice(0,3).join(' ') }) }
      else w.push({ date: '', dayNum: '', workCount: 0, partTimeWorkCount: 0, restCount: 0, shiftSummary: '' })
      c.setDate(c.getDate() + 1)
    }
    ws.push(w)
  }
  weeks.value = ws
}

// P3-39: 切换计划时重置所有状态
function selectPlan(row) {
  if (!row) return
  currentPlan.value = row
  planId.value = row.id
  weekStart.value = row.startDate || ''
  selectedDate.value = ''
  dayDate.value = ''
  dailyRows.value = []
  dailyIssues.value = []
  highlightedEmpId.value = null
  monthRows.value = []
  issuesList.value = []
  wsFilter.value = ''
  typeFilter.value = ''
  // 审查修复（P1-6）：切换计划时清空撤销栈与范围工具条，防止撤销/工具条作用到错误计划
  undoStack.value = []
  undoBar.visible = false
  clearRangeSel()
  // 保持当前视图，各视图按新方案自动重新加载；日明细日期会自动落到新方案周期内
  loadAll()
}

async function loadPlans() {
  plansLoading.value = true
  try {
    const res = await getSchedules({ page: 1, pageSize: 100 })
    plans.value = res.items || []
    // 自动选中当前 planId 对应的排班方案：高亮表格行并记录其周期范围
    if (planId.value) {
      const matched = plans.value.find(p => String(p.id) === String(planId.value))
      if (matched) {
        currentPlan.value = matched
        nextTick(() => plansTableRef.value?.setCurrentRow(matched))
      }
    }
  } catch (e) {
    /* 拦截器已提示 */
  } finally { plansLoading.value = false }
}

const issueStats = computed(() => {
  const list = issuesList.value
  return { gapCount: list.filter(i => i.issueType === 'STAFFING_GAP').length, warnCount: list.filter(i => i.severity === 'WARN').length, daysCount: new Set(list.map(i => i.workDate).filter(Boolean)).size }
})

const COLORS_ARR = ['#f56c6c','#e6a23c','#409eff','#67c23a','#909399']
const typePieData = computed(() => {
  const map = {}; issuesList.value.forEach(i => { map[i.issueType] = (map[i.issueType] || 0) + 1 })
  const entries = Object.entries(map); const total = entries.reduce((s, [,c]) => s + c, 0) || 1
  return entries.map(([k, c], idx) => ({ key: k, label: k === 'STAFFING_GAP' ? '岗位缺口' : k === 'SKILL_MISMATCH' ? '技能不匹配' : k === 'OVERTIME' ? '工时超限' : k === 'CONSECUTIVE_WORK' ? '连续工作超限' : k, count: c, pct: c / total, color: COLORS_ARR[idx % COLORS_ARR.length] }))
})
const typePieOffset = computed(() => { let sum = 0; return typePieData.value.map(s => { const v = -sum * 502.65; sum += s.pct; return v }) })
const typePieLabels = computed(() => { const r = 63; let cum = -Math.PI / 2; return typePieData.value.map(s => { const half = s.pct * Math.PI; const mid = cum + half; const x = 100 + r * Math.cos(mid); const y = 100 + r * Math.sin(mid); cum += s.pct * 2 * Math.PI; return { x: Math.round(x), y: Math.round(y), count: s.count } }) })

// P3-32: 用完整总数计算比例，再取前8，并补「其他」段使环形闭合
const wsPieData = computed(() => {
  const map = {}; issuesList.value.filter(i => i.issueType === 'STAFFING_GAP').forEach(i => { const ws = i.workstationName || '未知'; map[ws] = (map[ws] || 0) + 1 })
  const allEntries = Object.entries(map).sort((a,b) => b[1]-a[1])
  const total = allEntries.reduce((s,[,c]) => s+c, 0) || 1
  const top = allEntries.slice(0, 8)
  const topSum = top.reduce((s,[,c]) => s + c, 0)
  const result = top.map(([name, count], idx) => ({ name, count, pct: count / total, color: COLORS_ARR[idx % COLORS_ARR.length] }))
  // 补「其他」段：前8之外的计数归入一段，保证比例总和=100%，环形闭合
  const restCount = total - topSum
  if (allEntries.length > 8 && restCount > 0) {
    result.push({ name: '其他', count: restCount, pct: restCount / total, color: '#c0c4cc' })
  }
  return result
})
const wsPieOffset = computed(() => { let sum = 0; return wsPieData.value.map(s => { const v = -sum * 502.65; sum += s.pct; return v }) })
const wsTotal = computed(() => wsPieData.value.reduce((s, d) => s + d.count, 0))
const wsPieLabels = computed(() => { const r = 63; let cum = -Math.PI / 2; return wsPieData.value.map(s => { const half = s.pct * Math.PI; const mid = cum + half; const x = 100 + r * Math.cos(mid); const y = 100 + r * Math.sin(mid); cum += s.pct * 2 * Math.PI; return { x: Math.round(x), y: Math.round(y), count: s.count } }) })

const wsFilter = ref('')
const typeFilter = ref('')

function filterTableByWs(wsName) { wsFilter.value = wsFilter.value === wsName ? '' : wsName }
function filterTableByType(type) { typeFilter.value = typeFilter.value === type ? '' : type }

const filteredIssues = computed(() => {
  let list = issuesList.value
  if (wsFilter.value) list = list.filter(i => (i.workstationName || '未知') === wsFilter.value)
  if (typeFilter.value) list = list.filter(i => i.issueType === typeFilter.value)
  return list
})

// 合理度 = 已满足需求 ÷ 总需求 × 100（量纲统一为“人”）
// 合理度：使用后端返回的每日 Rationality（实际排班覆盖 ÷ 需求）
const rationalityData = computed(() => {
  if (rationalityList.value && rationalityList.value.length) {
    return rationalityList.value.map(r => ({ date: r.date, pct: r.pct }))
  }
  return []
})
function renderRationalityChart() {
  if (!rationalityChartRef.value || rationalityData.value.length === 0) return
  if (!rationalityChart) {
    rationalityChart = echarts.init(rationalityChartRef.value)
  }
  const dates = rationalityData.value.map(r => r.date.substring(5))
  const values = rationalityData.value.map(r => r.pct)
  rationalityChart.setOption({
    grid: { left: 45, right: 20, top: 20, bottom: 30 },
    tooltip: { trigger: 'axis', formatter: params => {
      const p = params[0]
      return rationalityData.value[p.dataIndex].date + '<br/>合理度：' + p.value + '%'
    }},
    xAxis: {
      type: 'category',
      data: dates,
      boundaryGap: false,
      axisLabel: { fontSize: 12, fontWeight: 'bold', color: '#303133' }
    },
    yAxis: {
      type: 'value',
      min: 0,
      max: 100,
      axisLabel: { fontSize: 12, fontWeight: 'bold', color: '#606266', formatter: '{value}%' },
      splitLine: { lineStyle: { color: '#ebeef5' } }
    },
    series: [{
      type: 'line',
      data: values,
      smooth: true,
      symbol: 'circle',
      symbolSize: 8,
      lineStyle: { width: 3, color: '#409eff' },
      itemStyle: { color: '#409eff', borderColor: '#fff', borderWidth: 2 },
      areaStyle: { color: 'rgba(64,158,255,0.15)' },
      label: { show: true, fontSize: 12, fontWeight: 'bold', color: '#303133', formatter: '{c}%' }
    }]
  })
}

watch(rationalityData, () => {
  nextTick(() => renderRationalityChart())
})

onMounted(() => {
  loadPlans()
  loadPreferenceMap() // P2：偏好匹配角标数据
  if (planId.value) loadAll()
  nextTick(() => renderRationalityChart())
  window.addEventListener('mousemove', onDragMove)
  window.addEventListener('mouseup', onDragEnd)
  window.addEventListener('keydown', onUndoKeydown)
})

onBeforeUnmount(() => {
  window.removeEventListener('mousemove', onDragMove)
  window.removeEventListener('mouseup', onDragEnd)
  window.removeEventListener('keydown', onUndoKeydown)
  document.removeEventListener('mousemove', onRangeMouseMove)
  document.removeEventListener('mouseup', onRangeMouseUp)
  clearTimeout(undoBarTimer)
  if (rationalityChart) {
    rationalityChart.dispose()
    rationalityChart = null
  }
})
</script>

<style scoped>
.gantt { border: 1px solid #ebeef5; border-radius: 4px; overflow-x: auto; }
.gantt-row { display: flex; border-bottom: 1px solid #ebeef5; min-width: 100%; }
.gantt-row-parttime .gantt-emp-col { background: #f7fdf5; }
.gantt-divider { background: #f0f9eb; color: #67c23a; font-weight: 600; font-size: 12px; padding: 5px 10px; border-bottom: 1px solid #c2e7b0; display: flex; align-items: center; gap: 6px; }
.gantt-divider-badge { display: inline-block; background: #67c23a; color: #fff; border-radius: 3px; font-size: 11px; padding: 0 5px; line-height: 16px; }
.gantt-header { background: #f5f7fa; font-weight: 600; }
.gantt-emp-col { width: 140px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; position: sticky; left: 0; background: #fff; z-index: 3; }
.gantt-header .gantt-emp-col { background: #f5f7fa; z-index: 4; }
.gantt-day-col { width: 150px; flex-shrink: 0; padding: 4px; border-right: 1px solid #ebeef5; box-sizing: border-box; }
.day-label { font-size: 12px; color: #606266; }
.day-sub { font-size: 11px; color: #909399; }
.day-block { border-radius: 4px; padding: 6px; text-align: center; font-size: 12px; height: 72px; box-sizing: border-box; overflow: hidden; }
.work-block { background: #ecf5ff; display: flex; flex-direction: column; align-items: center; justify-content: center; }
.rest-block { background: #fef0f0; color: #f56c6c; font-weight: 600; }
.empty-block { background: #fafafa; }
.shift-code { font-weight: 600; }
.shift-time { font-size: 11px; color: #909399; }
.shift-break { margin-top: 2px; font-size: 10px; color: #e6a23c; line-height: 1.4; }
.emp-name { font-size: 12px; font-weight: 600; }
.emp-sub { font-size: 11px; color: #909399; }
.calendar { border: 1px solid #ebeef5; border-radius: 4px; }
.cal-header { display: flex; background: #f5f7fa; }
.cal-header-cell { flex: 1; text-align: center; padding: 4px 6px 0; font-weight: 600; font-size: 13px; }
.cal-week { display: flex; border-bottom: 1px solid #ebeef5; }
.cal-week:last-child { border-bottom: none; }
.cal-cell { flex: 1; min-height: 72px; padding: 6px; border-right: 1px solid #ebeef5; cursor: pointer; }
.cal-cell:hover { background: #ecf5ff; }
.cal-cell.is-empty { background: #fafafa; cursor: default; }
.cal-cell.is-selected { background: #e6f7ff; box-shadow: inset 0 0 0 2px #409eff; }
.cal-day-num { font-size: 14px; font-weight: 600; margin-bottom: 4px; }
.cal-work { font-size: 12px; color: #409eff; }
.cal-parttime { font-size: 12px; color: #67c23a; }
.cal-rest { font-size: 12px; color: #f56c6c; }
.cal-shift { font-size: 11px; color: #909399; margin-top: 4px; }
.matrix-wrap { overflow: auto; max-height: 560px; position: relative; }
.matrix { border: 1px solid #ebeef5; border-radius: 4px; }
.m-row { display: flex; border-bottom: 1px solid #ebeef5; }
.m-row:last-child { border-bottom: none; }
.m-header { background: #f5f7fa; font-weight: 600; position: sticky; top: 0; z-index: 4; }
.m-ws-col { width: 130px; flex-shrink: 0; padding: 6px 8px; border-right: 1px solid #ebeef5; display: flex; align-items: center; position: sticky; left: 0; background: #fff; z-index: 3; }
.m-header .m-ws-col { background: #f5f7fa; z-index: 5; }
.m-slot-col { width: 72px; min-height: 48px; flex-shrink: 0; padding: 2px 3px; border-right: 1px solid #f5f7fa; font-size: 11px; text-align: center; position: relative; }
.m-slot-col:last-child { border-right: none; }
/* P3 空位加人：空格子可点击，悬停显示 + 提示；滑动选择多时段 */
.m-slot-col.is-empty { cursor: pointer; }
.m-slot-col.is-empty:hover { background: rgba(64, 158, 255, 0.08); }
.m-slot-col.is-empty:hover::after { content: '+'; position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); color: #409eff; font-size: 16px; font-weight: 600; pointer-events: none; }
.m-slot-col.range-selected { background: rgba(64, 158, 255, 0.18); outline: 1px solid #409eff; outline-offset: -1px; }
.range-hint { position: absolute; top: 6px; left: 6px; z-index: 20; background: #409eff; color: #fff; font-size: 12px; padding: 4px 12px; border-radius: 4px; width: fit-content; pointer-events: none; }
.range-toolbar { position: absolute; top: 6px; left: 6px; z-index: 25; display: flex; align-items: center; gap: 8px; background: #fff; border: 1px solid #409eff; border-radius: 6px; padding: 6px 10px; box-shadow: 0 2px 12px rgba(0,0,0,0.15); }
.range-toolbar .rt-info { font-size: 12px; color: #409eff; font-weight: 600; white-space: nowrap; }
.matrix { user-select: none; }
.has-employee { background: #ecf5ff; }
.next-day { background: #fdf6ec; }
.has-gap { box-shadow: inset 0 0 0 2px #f56c6c; }
.has-gap-low-skill { box-shadow: inset 0 0 0 2px #67c23a; background: #f0f9eb; }
.gap-flag { position: absolute; top: 1px; right: 1px; background: #f56c6c; color: #fff; font-size: 10px; border-radius: 2px; padding: 0 3px; line-height: 14px; }
.gap-flag-low { background: #67c23a; }
.emp-chip { background: #409eff; color: #fff; border-radius: 3px; padding: 2px 4px; margin-bottom: 2px; font-size: 11px; }
.emp-chip .emp-name { font-weight: 600; }
/* P2：偏好匹配角标——与店长历史偏好一致的色块显示绿点 */
.pref-dot { display: inline-block; width: 6px; height: 6px; border-radius: 50%; background: #67c23a; margin-left: 3px; vertical-align: middle; box-shadow: 0 0 0 1px #fff; }
.emp-chip .emp-shift { opacity: 0.85; font-size: 10px; }
/* 兼职色块：绿色，且与前面的全职色块用虚线间隔隔开 */
.emp-chip.is-parttime { background: #67c23a; }
.emp-chip.pt-first { border-top: 1px dashed #a3d98a; padding-top: 3px; margin-top: 1px; }
/* 班中休息色块：灰色 + 橙色「休」标记，第二行显示休息时间段与顶岗人 */
.emp-chip.is-break { background: #909399; }
.emp-chip.is-break .break-info { color: #ffe6a7; }
.break-flag { display: inline-block; background: #e6a23c; color: #fff; border-radius: 2px; padding: 0 3px; margin-left: 4px; font-size: 10px; line-height: 14px; }
.next-day .emp-chip { background: #e6a23c; }
.next-day .emp-chip.is-parttime { background: #67c23a; }
.next-day .emp-chip.is-break { background: #909399; }
/* 单击高亮：该员工当天全部工作色块——亮黄底 + 白边 + 呼吸光晕，其余色块压暗 */
.emp-chip.is-highlighted {
  background: #ffb800 !important;
  color: #303133;
  font-weight: 600;
  box-shadow: 0 0 0 2px #fff, 0 0 0 4px #ffb800, 0 0 16px 3px rgba(255, 184, 0, 0.9);
  z-index: 2;
  animation: chip-highlight-pulse 1.4s ease-in-out infinite;
}
.emp-chip.is-highlighted:hover { opacity: 1; }
.emp-chip.is-highlighted .emp-shift { color: #303133; opacity: 1; }
.emp-chip.is-highlighted .break-flag { background: #e6a23c; }
@keyframes chip-highlight-pulse {
  0%, 100% { box-shadow: 0 0 0 2px #fff, 0 0 0 4px #ffb800, 0 0 10px 2px rgba(255, 184, 0, 0.8); }
  50% { box-shadow: 0 0 0 2px #fff, 0 0 0 4px #ffb800, 0 0 24px 7px rgba(255, 184, 0, 1); }
}
/* 有高亮时压暗其他员工色块，突出被高亮员工的全部安排 */
.matrix.has-chip-highlight .emp-chip:not(.is-highlighted) { opacity: 0.35; transition: opacity 0.2s; }
.matrix.has-chip-highlight .emp-chip:not(.is-highlighted):hover { opacity: 0.75; }
.emp-chip { cursor: grab; }
.emp-chip:hover { opacity: 0.85; }

/* ============ 拖动移动工作段 ============ */
.drag-ghost {
  position: fixed;
  z-index: 3000;
  pointer-events: none;
  background: rgba(64, 158, 255, 0.78);
  color: #fff;
  border: 1px dashed #fff;
  border-radius: 3px;
  font-size: 11px;
  padding: 3px 8px;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.28);
  display: flex;
  align-items: center;
  overflow: hidden;
  white-space: nowrap;
}
.m-slot-col.snap-target {
  outline: 2px dashed #409eff;
  outline-offset: -2px;
  background: rgba(64, 158, 255, 0.14) !important;
}

.ds-info { font-size: 13px; color: #303133; }
.ds-label { color: #909399; }
.stat-num { font-size: 28px; font-weight: 700; color: #303133; }
.stat-label { font-size: 13px; color: #909399; margin-top: 4px; }
.legend-row { display: flex; align-items: center; gap: 6px; font-size: 12px; }
.legend-row.clickable { cursor: pointer; user-select: none; }
.legend-row.clickable:hover { background: #f5f7fa; border-radius: 4px; }
.legend-dot { width: 12px; height: 12px; border-radius: 50%; display: inline-block; }
.mini-legend { margin-top: 4px; }
.parttime-block { border: 1px solid #67c23a; border-radius: 6px; padding: 12px; background: #f0f9eb; }
.parttime-title { display: flex; align-items: center; gap: 6px; font-weight: 600; color: #303133; margin-bottom: 10px; }
.parttime-badge { display: inline-block; background: #67c23a; color: #fff; border-radius: 3px; font-size: 12px; padding: 0 6px; line-height: 18px; }
.parttime-table { border: 1px solid #c2e7b0; border-radius: 4px; overflow: hidden; }
.parttime-row { display: flex; border-bottom: 1px solid #e8f5e0; }
.parttime-row:last-child { border-bottom: none; }
.parttime-header { background: #f0f9eb; font-weight: 600; }
.parttime-ws-col { width: 90px; flex-shrink: 0; padding: 5px 8px; border-right: 1px solid #e8f5e0; font-size: 12px; }
.parttime-day-col { flex: 1; min-height: 20px; padding: 3px; border-right: 1px solid #e8f5e0; font-size: 11px; text-align: center; color: #909399; }
.parttime-day-col:last-child { border-right: none; }
.parttime-day-col.active { background: #67c23a; }
.parttime-legend { margin-top: 8px; display: flex; align-items: center; gap: 6px; font-size: 12px; color: #606266; }
.parttime-legend-box { background: #67c23a; }
/* P2 交互：批量模式 */
.batch-bar { display: flex; align-items: center; gap: 8px; margin: 8px 0; font-size: 12px; color: #606266; }
.batch-label { color: #909399; }
.matrix.batch-mode .m-slot-col { cursor: crosshair; }
.m-slot-col.batch-selected { outline: 2px solid #e6a23c; outline-offset: -2px; background: rgba(230, 162, 60, 0.15); }
/* P0 交互：调整撤销条 */
.undo-bar {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 3000;
  background: #303133;
  color: #fff;
  border-radius: 6px;
  padding: 8px 16px;
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
}
</style>
