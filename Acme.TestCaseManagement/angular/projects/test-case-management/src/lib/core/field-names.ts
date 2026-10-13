import { I18nService } from './i18n/i18n';

/**
 * The fields that the server names ("Title, AcceptanceCriteria") in the language of the person: the key is `field.` and the name
 * with a small first letter; a name without a text is shown as it came.
 */
export function fieldList(i18n: I18nService, names: string | null): string {
  return (names ?? '').split(',').map(name => name.trim()).filter(Boolean).map(name => {
    const key = `field.${name.charAt(0).toLowerCase()}${name.slice(1)}`;
    const text = i18n.t(key);
    return text === key ? name : text;
  }).join(', ');
}
