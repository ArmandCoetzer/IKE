import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../models/job_card.dart';
import '../services/job_permits_service.dart';

/// Structured fields + commitment checklists for child (non–Work Authorisation) permits.
class ChildPermitFormScreen extends StatefulWidget {
  final JobPermitDto permit;

  const ChildPermitFormScreen({super.key, required this.permit});

  @override
  State<ChildPermitFormScreen> createState() => _ChildPermitFormScreenState();
}

class _ChildPermitFormScreenState extends State<ChildPermitFormScreen> {
  final _service = JobPermitsService();
  final _formKey = GlobalKey<FormState>();
  final Map<String, bool> _checklistChecked = {};
  final Map<String, TextEditingController> _textControllers = {};
  final Map<String, bool> _boolValues = {};
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    for (final c in widget.permit.checklistItems) {
      _checklistChecked[c.id] = c.checked;
    }
    final fields = widget.permit.formFields ?? [];
    final existing = widget.permit.formValues ?? {};
    for (final f in fields) {
      if (f.type.toLowerCase() == 'bool') {
        final v = existing[f.id];
        _boolValues[f.id] =
            v == 'true' || v == '1' || (v != null && v.toLowerCase() == 'yes');
      } else {
        _textControllers[f.id] = TextEditingController(text: existing[f.id] ?? '');
      }
    }
  }

  @override
  void dispose() {
    for (final t in _textControllers.values) {
      t.dispose();
    }
    super.dispose();
  }

  bool _isNumberType(String type) {
    final t = type.toLowerCase();
    return t == 'number' || t == 'int' || t == 'integer' || t == 'decimal' || t == 'float' || t == 'double';
  }

  Future<void> _pickDate(String fieldId) async {
    final c = _textControllers[fieldId];
    if (c == null) return;
    DateTime initial = DateTime.now();
    final raw = c.text.trim();
    if (raw.length >= 10) {
      initial = DateTime.tryParse(raw.substring(0, 10)) ?? initial;
    }
    final d = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime(initial.year - 1),
      lastDate: DateTime(initial.year + 2),
    );
    if (d != null) {
      c.text =
          '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
      setState(() {});
    }
  }

  Future<void> _pickTime(String fieldId) async {
    final c = _textControllers[fieldId];
    if (c == null) return;
    var initial = TimeOfDay.now();
    final raw = c.text.trim();
    final m = RegExp(r'^(\d{1,2}):(\d{2})').firstMatch(raw);
    if (m != null) {
      final h = int.tryParse(m.group(1)!) ?? 0;
      final min = int.tryParse(m.group(2)!) ?? 0;
      initial = TimeOfDay(hour: h.clamp(0, 23), minute: min.clamp(0, 59));
    }
    final t = await showTimePicker(context: context, initialTime: initial);
    if (t != null) {
      c.text =
          '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
      setState(() {});
    }
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    for (final c in widget.permit.checklistItems) {
      if (_checklistChecked[c.id] != true) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Acknowledge every safety commitment.')),
        );
        return;
      }
    }

    final form = <String, String>{};
    for (final f in widget.permit.formFields ?? []) {
      final type = f.type.toLowerCase();
      if (type == 'bool') {
        final on = _boolValues[f.id] == true;
        form[f.id] = on ? 'true' : 'false';
        if (f.required && !on) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Confirm: ${f.label}')),
          );
          return;
        }
      } else {
        final v = _textControllers[f.id]?.text.trim() ?? '';
        if (f.required && v.isEmpty) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Required: ${f.label}')),
          );
          return;
        }
        if (v.isNotEmpty) {
          if (type == 'date' && !RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(v)) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text('Invalid date for ${f.label}')),
            );
            return;
          }
          if (type == 'time' && !RegExp(r'^\d{1,2}:\d{2}').hasMatch(v)) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text('Invalid time for ${f.label}')),
            );
            return;
          }
          if (_isNumberType(f.type)) {
            if (type == 'decimal' || type == 'float' || type == 'double') {
              if (double.tryParse(v.replaceAll(',', '.')) == null) {
                ScaffoldMessenger.of(context).showSnackBar(
                  SnackBar(content: Text('Enter a valid number for ${f.label}')),
                );
                return;
              }
            } else if (int.tryParse(v) == null) {
              ScaffoldMessenger.of(context).showSnackBar(
                SnackBar(content: Text('Enter a whole number for ${f.label}')),
              );
              return;
            }
          }
        }
        form[f.id] = v;
      }
    }

    final items = widget.permit.checklistItems
        .map((c) => <String, dynamic>{'id': c.id, 'label': c.label, 'checked': true})
        .toList();

    setState(() => _saving = true);
    try {
      await _service.submitPermitChecklist(
        widget.permit.id,
        items: items,
        form: form.isEmpty ? null : form,
      );
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.toString())),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final name = widget.permit.permitTemplateName ?? 'Permit';
    final fields = widget.permit.formFields ?? [];
    final byGroup = <String, List<PermitFormFieldSchemaDto>>{};
    for (final f in fields) {
      byGroup.putIfAbsent(f.group ?? 'Details', () => []).add(f);
    }
    final groupOrder = byGroup.keys.toList()..sort();

    return Scaffold(
      appBar: AppBar(title: Text(name)),
      body: Form(
        key: _formKey,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              'Complete all fields and commitments before client sign-off (upload/signature).',
              style: TextStyle(fontSize: 13, color: Colors.grey[700]),
            ),
            const SizedBox(height: 16),
            if (widget.permit.checklistItems.isNotEmpty) ...[
              Text('My commitment to safety', style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 8),
              ...widget.permit.checklistItems.map(
                (c) => CheckboxListTile(
                  value: _checklistChecked[c.id] ?? false,
                  onChanged: (v) => setState(() => _checklistChecked[c.id] = v ?? false),
                  title: Text(c.label, style: const TextStyle(fontSize: 14)),
                  controlAffinity: ListTileControlAffinity.leading,
                ),
              ),
              const SizedBox(height: 24),
            ],
            for (final g in groupOrder) ...[
              Text(g, style: Theme.of(context).textTheme.titleSmall),
              const SizedBox(height: 8),
              ...byGroup[g]!.map(_buildField),
              const SizedBox(height: 16),
            ],
            FilledButton(
              onPressed: _saving ? null : _submit,
              child: _saving
                  ? const SizedBox(
                      height: 22,
                      width: 22,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Save & sign off (permit manager)'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildField(PermitFormFieldSchemaDto f) {
    final t = f.type.toLowerCase();
    if (t == 'bool') {
      return SwitchListTile(
        title: Text(f.label),
        subtitle: f.required ? const Text('Required confirmation') : null,
        value: _boolValues[f.id] ?? false,
        onChanged: (v) => setState(() => _boolValues[f.id] = v),
      );
    }
    if (t == 'textarea') {
      return Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextFormField(
          controller: _textControllers[f.id],
          decoration: InputDecoration(
            labelText: '${f.label}${f.required ? ' *' : ''}',
            border: const OutlineInputBorder(),
          ),
          maxLines: 4,
          validator: f.required
              ? (v) => (v == null || v.trim().isEmpty) ? 'Required' : null
              : null,
        ),
      );
    }
    if (t == 'date') {
      final c = _textControllers[f.id];
      return Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextFormField(
          controller: c,
          readOnly: true,
          decoration: InputDecoration(
            labelText: '${f.label}${f.required ? ' *' : ''}',
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: const Icon(Icons.calendar_today),
              onPressed: () => _pickDate(f.id),
            ),
          ),
          onTap: () => _pickDate(f.id),
          validator: (v) {
            if (!f.required && (v == null || v.trim().isEmpty)) return null;
            if (v == null || v.trim().isEmpty) return 'Required';
            if (!RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(v.trim())) return 'Pick a date';
            return null;
          },
        ),
      );
    }
    if (t == 'time') {
      final c = _textControllers[f.id];
      return Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextFormField(
          controller: c,
          readOnly: true,
          decoration: InputDecoration(
            labelText: '${f.label}${f.required ? ' *' : ''}',
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: const Icon(Icons.access_time),
              onPressed: () => _pickTime(f.id),
            ),
          ),
          onTap: () => _pickTime(f.id),
          validator: (v) {
            if (!f.required && (v == null || v.trim().isEmpty)) return null;
            if (v == null || v.trim().isEmpty) return 'Required';
            if (!RegExp(r'^\d{1,2}:\d{2}').hasMatch(v.trim())) return 'Pick a time';
            return null;
          },
        ),
      );
    }
    if (_isNumberType(f.type)) {
      final decimal = t == 'decimal' || t == 'float' || t == 'double';
      return Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextFormField(
          controller: _textControllers[f.id],
          keyboardType: TextInputType.numberWithOptions(decimal: decimal, signed: false),
          inputFormatters: decimal
              ? [FilteringTextInputFormatter.allow(RegExp(r'[\d.,]'))]
              : [FilteringTextInputFormatter.digitsOnly],
          decoration: InputDecoration(
            labelText: '${f.label}${f.required ? ' *' : ''}',
            border: const OutlineInputBorder(),
          ),
          validator: (v) {
            if (!f.required && (v == null || v.trim().isEmpty)) return null;
            if (v == null || v.trim().isEmpty) return 'Required';
            if (decimal) {
              if (double.tryParse(v.trim().replaceAll(',', '.')) == null) return 'Enter a valid number';
            } else if (int.tryParse(v.trim()) == null) {
              return 'Enter a whole number';
            }
            return null;
          },
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: TextFormField(
        controller: _textControllers[f.id],
        decoration: InputDecoration(
          labelText: '${f.label}${f.required ? ' *' : ''}',
          border: const OutlineInputBorder(),
        ),
        validator: f.required
            ? (v) => (v == null || v.trim().isEmpty) ? 'Required' : null
            : null,
      ),
    );
  }
}
