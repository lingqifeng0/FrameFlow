param([long]$TestWindow, [string]$Value, [string]$Checkbox = '', [string]$InvokeId = '', [string]$InvokeName = '', [string]$Search = '', [switch]$ReadTree)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$testElement = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$TestWindow)
if ($ReadTree) {
    $testCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem)
    $testItems = $testElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $testCondition)
    $testResult = foreach ($testItem in $testItems) {
        $testPattern = $null
        $testExpandState = if ($testItem.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$testPattern)) { $testPattern.Current.ExpandCollapseState.ToString() } else { 'LeafNode' }
        [PSCustomObject]@{ name = $testItem.Current.Name; expanded = $testExpandState }
    }
    ConvertTo-Json -InputObject @($testResult) -Compress
} elseif ($InvokeId -or $InvokeName) {
    $testProperty = if ($InvokeId) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
    $testValue = if ($InvokeId) { $InvokeId } else { $InvokeName }
    $testCondition = New-Object System.Windows.Automation.PropertyCondition($testProperty, $testValue)
    $testButton = $testElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $testCondition)
    if (!$testButton) { throw "Test action not found: $testValue" }
    $testPattern = $null
    if ($testButton.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$testPattern)) {
        $testPattern.Invoke()
    } else {
        $testPattern = $testButton.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
        $testPattern.DoDefaultAction()
    }
} elseif ($Search) {
    $testCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'SearchTextBox')
    $testBox = $testElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $testCondition)
    if (!$testBox) { throw 'Settings search not found' }
    $testPattern = $testBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $testPattern.SetValue($Search)
} elseif ($Checkbox) {
    $testNameCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Checkbox)
    $testTypeCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
    $testCondition = New-Object System.Windows.Automation.AndCondition($testNameCondition, $testTypeCondition)
    $testCheck = $testElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $testCondition)
    if (!$testCheck) { throw 'Test checkbox not found' }
    $testPattern = $testCheck.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $testPattern.Toggle()
} else {
    $testCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ValueTextBox')
    $testBoxes = $testElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $testCondition)
    $testBox = $null
    foreach ($testCandidate in $testBoxes) {
        if (!$testCandidate.Current.IsOffscreen) { $testBox = $testCandidate; break }
    }
    if (!$testBox) { throw 'Visible setting value not found' }
    $testPattern = $testBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $testPattern.SetValue($Value)
}
