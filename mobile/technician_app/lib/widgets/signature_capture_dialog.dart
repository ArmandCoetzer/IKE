import 'package:flutter/material.dart';
import 'package:signature/signature.dart';

/// Dialog to capture a client signature and return PNG bytes.
Future<List<int>?> showSignatureCaptureDialog(BuildContext context, {String title = 'Client signature'}) async {
  return showDialog<List<int>>(
    context: context,
    barrierDismissible: false,
    builder: (context) => _SignatureCaptureDialog(title: title),
  );
}

class _SignatureCaptureDialog extends StatefulWidget {
  final String title;

  const _SignatureCaptureDialog({required this.title});

  @override
  State<_SignatureCaptureDialog> createState() => _SignatureCaptureDialogState();
}

class _SignatureCaptureDialogState extends State<_SignatureCaptureDialog> {
  late final SignatureController _controller;

  @override
  void initState() {
    super.initState();
    _controller = SignatureController(
      penStrokeWidth: 3,
      penColor: Colors.black,
      exportBackgroundColor: Colors.white,
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _confirm() async {
    if (_controller.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Please sign before confirming')),
      );
      return;
    }
    final bytes = await _controller.toPngBytes();
    if (!mounted) return;
    Navigator.of(context).pop(bytes != null && bytes.isNotEmpty ? bytes : null);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text(widget.title),
      content: SizedBox(
        width: 320,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Sign in the box below',
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: Colors.grey),
            ),
            const SizedBox(height: 12),
            Container(
              decoration: BoxDecoration(
                border: Border.all(color: Colors.grey.shade300),
                borderRadius: BorderRadius.circular(8),
              ),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(8),
                child: Signature(
                  controller: _controller,
                  width: 300,
                  height: 180,
                  backgroundColor: Colors.white,
                ),
              ),
            ),
            const SizedBox(height: 8),
            TextButton.icon(
              onPressed: () => _controller.clear(),
              icon: const Icon(Icons.refresh, size: 18),
              label: const Text('Clear'),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _confirm,
          child: const Text('Done'),
        ),
      ],
    );
  }
}
